# News feed samples

WP05 needs one real, unedited sample of each feed here before the parsers are
written. The owner downloads them once and commits them:

- `forexfactory-thisweek.json` — the Forex Factory weekly JSON export.
- `showmytrades-calendar.json` — one response of the ShowMyTrades calendar API
  (JSON), with `impact=all` and `days=7`.
- `forexfactory-denied.html` — optional: the "Request Denied" page returned
  when the rate limit is exceeded, for the rejection test.

Do not write these files from memory. The parsers must be built against what
the feeds really return.
