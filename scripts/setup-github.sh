#!/usr/bin/env bash
# One-time GitHub configuration for this repository.
# Run it yourself, from a machine where `gh auth status` shows you logged in
# with admin rights on the repository. Read it before running it.
#
#   ./scripts/setup-github.sh <owner>/<repo>
#
# What it does
#   1. Creates the labels used by the workflow.
#   2. Turns on "delete branch on merge" and squash-only merging.
#   3. Creates a ruleset on the default branch: pull request required, the four
#      status checks required, no force push, no deletion.
#
# IMPORTANT: on a PRIVATE repository, GitHub enforces rulesets and branch
# protection only on a paid plan (GitHub Pro for a personal account, or Team
# and above). On GitHub Free the ruleset is created but not enforced.
#
# REQUIRED_APPROVALS: leave 0 when the agents open pull requests under your
# own GitHub account (GitHub does not let an author approve their own pull
# request). Set to 1 only if pull requests come from a separate bot account.

set -euo pipefail

REPO="${1:?usage: setup-github.sh <owner>/<repo>}"
REQUIRED_APPROVALS="${REQUIRED_APPROVALS:-0}"

echo "==> Labels"
gh label create "human-approved-infra" --repo "$REPO" --color B60205 \
  --description "Owner approves changes to protected paths in this pull request" --force
gh label create "work-package" --repo "$REPO" --color 0E8A16 \
  --description "Implements one work package" --force
gh label create "validator-fail" --repo "$REPO" --color D93F0B \
  --description "Validator verdict was FAIL" --force
gh label create "dependencies" --repo "$REPO" --color 0366D6 \
  --description "Dependency update" --force

echo "==> Merge settings"
gh api --method PATCH "repos/$REPO" \
  -F delete_branch_on_merge=true \
  -F allow_squash_merge=true \
  -F allow_merge_commit=false \
  -F allow_rebase_merge=false >/dev/null

echo "==> Ruleset on the default branch"
RULESET=$(cat <<JSON
{
  "name": "main: pull request and checks required",
  "target": "branch",
  "enforcement": "active",
  "conditions": { "ref_name": { "include": ["~DEFAULT_BRANCH"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    {
      "type": "pull_request",
      "parameters": {
        "required_approving_review_count": ${REQUIRED_APPROVALS},
        "dismiss_stale_reviews_on_push": true,
        "require_code_owner_review": false,
        "require_last_push_approval": false,
        "required_review_thread_resolution": true
      }
    },
    {
      "type": "required_status_checks",
      "parameters": {
        "strict_required_status_checks_policy": true,
        "required_status_checks": [
          { "context": "build-test" },
          { "context": "repo-checks" },
          { "context": "protected-paths" },
          { "context": "validator" }
        ]
      }
    }
  ]
}
JSON
)
echo "$RULESET" | gh api --method POST "repos/$REPO/rulesets" --input - >/dev/null \
  && echo "    ruleset created" \
  || echo "    could not create the ruleset (already exists, or the plan does not allow it)"

cat <<'NEXT'

Done. Still to do by hand:
  1. Optional, only if the validator workflow is switched on:
     Settings > Secrets and variables > Actions > New repository secret:
       ANTHROPIC_API_KEY   (for the validator workflow)
     Optional variable: VALIDATOR_MODEL
  2. Settings > Actions > General > Workflow permissions: "Read repository
     contents" (the workflows ask for what they need).
  3. Open one pull request and confirm the checks appear (build-test,
     repo-checks, protected-paths, and validator when it is on), then confirm in
     Settings > Rules that the ruleset shows as enforced.
NEXT
