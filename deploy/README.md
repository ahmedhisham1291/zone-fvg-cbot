# Deployment guide: laptop stage and VPS

How to run the Zone + FVG cBot first on your laptop (spec 10.5) and then on a
rented Linux server (spec 10.2). Written for an owner who is not a server
administrator. Follow the steps in order and do not skip a check.

- What to do when an alert arrives, weekly checks, halting and resuming:
  [`docs/runbook.md`](../docs/runbook.md).
- The 14 fault-injection tests you must pass before the Challenge:
  [`docs/test-records/WP11-fault-injection.md`](../docs/test-records/WP11-fault-injection.md).

## How to read this guide

- Every command has a link to the documentation it comes from, in the line
  `Source:` under it. If a command fails, read that page first.
- Text in `CAPITALS_LIKE_THIS` or with `your-` in front is a placeholder you
  replace. Never paste a real password, token or account number into a chat,
  an issue, a pull request or a file inside the repository.
- **To check on first start** marks something the documentation does not say.
  It is checked once, on a demo account, before any real use. Write the answer
  in the test record (`docs/test-records/WP11-fault-injection.md`, section
  "First-start checks").
- Use the laptop for setting up the server (copying files needs a keyboard).
  The phone is enough for monitoring: Telegram alerts, the heartbeat monitor and
  the cTrader mobile app.
- Commands that start with `sudo` run as administrator. Read them before you
  press Enter.

## Documentation used

| Topic | Page |
|---|---|
| cTrader CLI: run a cBot, Docker example, `.cbotset` file, `--full-access`, `--exit-on-stop` | https://help.ctrader.com/ctrader-cli/cbots/ |
| cTrader CLI: install, password file, .NET 8 requirement | https://help.ctrader.com/ctrader-cli/setup/ |
| cTrader CLI: FAQ (`.algo` must be built for .NET 8, `.cbotset` files) | https://help.ctrader.com/ctrader-cli/faq/ |
| cTrader CLI: troubleshooting | https://help.ctrader.com/ctrader-cli/troubleshooting/ |
| cTrader CLI: run a cBot around the clock | https://help.ctrader.com/ctrader-cli/use-cases/run-a-cbot-around-the-clock/ |
| cTrader CLI: full command and option reference | https://github.com/spotware/CLI-references (README, read 2026-11-16, commit `12c6042`) |
| cTrader CLI: official Docker image, tags, environment variables, Compose example | https://github.com/spotware/ctrader-console-docker (README, read 2026-11-16, commit `ea9a6f0`) |
| cTrader Algo: local storage (folders, saved every minute and on stop) | https://help.ctrader.com/ctrader-algo/guides/local-storage/ |
| cTrader Algo: network access works with `AccessRights.None` | https://help.ctrader.com/ctrader-algo/guides/network-access/ |
| Docker Engine on Ubuntu | https://docs.docker.com/engine/install/ubuntu/ |
| Docker post-installation (docker group) | https://docs.docker.com/engine/install/linux-postinstall/ |
| Compose file attributes | https://docs.docker.com/reference/compose-file/services/ (same text: https://github.com/compose-spec/compose-spec, `container_spec.md`, `05-services.md`, `09-secrets.md`) |
| Compose commands | https://docs.docker.com/reference/cli/docker/compose/ |
| json-file log rotation | https://docs.docker.com/engine/logging/drivers/json-file/ |
| Ubuntu users | https://ubuntu.com/server/docs/how-to/security/user-management/ |
| Ubuntu OpenSSH server | https://ubuntu.com/server/docs/how-to/security/openssh-server/ |
| sshd settings | https://man7.org/linux/man-pages/man5/sshd_config.5.html |
| Ubuntu firewall (ufw) | https://ubuntu.com/server/docs/how-to/security/firewalls/ and https://help.ubuntu.com/community/UFW |
| Ubuntu automatic updates | https://ubuntu.com/server/docs/how-to/software/automatic-updates/ |
| Ubuntu updates and reboots | https://ubuntu.com/blog/ubuntu-updates-best-practices-for-updating-your-instance |
| Ubuntu time synchronisation | https://ubuntu.com/server/docs/how-to/networking/timedatectl-and-timesyncd/ , https://ubuntu.com/server/docs/how-to/networking/chrony-client/ , https://ubuntu.com/server/docs/explanation/networking/about-time-synchronisation/ |
| OpenSSH client on Windows | https://learn.microsoft.com/windows-server/administration/openssh/openssh_install_firstuse |
| scp | https://man.openbsd.org/scp |
| Standard Linux tools used below: `cd`, `cp`, `mkdir`, `chmod`, `chown`, `find`, `printf`, `date`, `df`, `su`, `passwd`, `systemctl`, `reboot` | https://man7.org/linux/man-pages/ (one page per tool, for example https://man7.org/linux/man-pages/man1/chmod.1.html) |
| `nano` text editor (Ctrl+O saves, Ctrl+X exits) | https://www.nano-editor.org/dist/latest/nano.html |

## Facts from the documentation that shape this setup

1. **Image.** The official image is `ghcr.io/spotware/ctrader-console`. Its
   README lists `5.9.11` as the newest stable tag and says tags other than
   `latest` are immutable. The compose file pins `5.9.11`. (The image's git
   repository also has a `5.10.1` tag, which the README does not list; from 5.10
   the image moves to .NET 10. Do not use it until it has passed the fault tests
   on demo, spec 8.6 and 10.2.)
2. **.NET version. Read this.** The CLI pages say the `.algo` file must be built
   for .NET 8, and "cTrader CLI does not support older .NET cBots". This
   project builds for `net6.0` (`AGENTS.md`). The image README says it carries
   reference packs for older .NET versions. Whether the CLI runs our `.algo` is
   **to check on first start** (laptop: "Start in external process"; VPS:
   step 12.2). If it refuses, stop and tell the developer: retargeting is a code
   change, not a deployment step.
3. **Password.** The CLI reads the cTrader ID password only from a file
   (`--pwd-file`, or `PWD_FILE` in the environment). `run` rejects
   `--password`. The compose file mounts the file as a Docker secret at
   `/run/secrets/ctrader_password`. The cTrader ID and account number come from
   an environment file (`/opt/zfvg/.env`).
4. **Parameters.** A `.cbotset` file is the second argument of `run`, or
   `CBOT_SET` in the environment. The documented example writes it as
   `Name=Value` lines; the FAQ says `.cbotset` files come from the desktop app.
   If the CLI reports "Invalid .cbotset file format", save the parameters from
   cTrader desktop instead (troubleshooting page). **To check on first start**:
   the bot refuses to start while `RiskEpochDate` is empty or invalid, so a
   successful start proves the file was read.
5. **Full access.** Not used. Our cBot declares `AccessRights.None`, which is
   enough for HTTP (network-access page) and local storage (local-storage
   page). The image README adds that on Linux there is no sandbox either way;
   leaving out `--full-access` only prints a warning and waits five seconds.
6. **Local storage and logs.** cTrader keeps local storage under
   `Documents/cAlgo/LocalStorage/...` in the user's home directory; an Instance
   scope value lives in a folder named after the instance's unique identifier.
   The documentation does not say where that is inside the container. The
   compose file sets `HOME=/data` on a named volume, so whatever the CLI writes
   under its home survives container re-creation. Everything the cBot prints
   goes to standard output (`docker compose logs`), rotated at 5 files of
   10 MB per container. **To check on first start** (step 12.5): the folder
   really is under `/data`, and a restarted container reuses the same instance
   folder. If every start creates a new folder, the news download limit across
   restarts (spec 6.2, fault test 14) and the daily-budget memory do not work
   under the CLI: stop and tell the developer.
7. **Restart.** `--exit-on-stop` makes the container exit when the cBot stops;
   without it a container with `restart: unless-stopped` never restarts after
   the cBot stops (image README, "Operating the container"). What the CLI does
   after a disconnect is not documented (spec 10.2); fault tests 1, 2 and 5
   prove it.

---

## Part A. Stage 1: your laptop (spec 10.5)

Goal: a few days on an FTMO Free Trial with cTrader desktop on your Mac,
before you pay for a server.

**Mac notes (the owner uses a Mac):**
- Use the native **cTrader for Mac** app. It runs cBots built for .NET 6, which
  is what CI produces (the `.algo` from the `ZoneFvgBot-algo` artifact). Sources:
  https://community.ctrader.com/forum/announcements/42478/ and
  https://community.ctrader.com/forum/ctrader-mac/43106/.
- Import the bot: download and unzip the artifact, rename `src.algo` to
  `ZoneFvgBot.algo`, then double-click it (or drag it onto the cTrader window,
  or use Algo > cBots > import). If cTrader asks to install the .NET SDK to
  build algos, allow it.
- Keep the Mac awake for the whole test: System Settings > Battery (or Energy)
  > prevent automatic sleeping when the display is off, keep it on power, and
  leave the lid open (a closed lid sleeps a Mac unless an external display is
  attached). As a backup, run `caffeinate -dimsu` in Terminal and leave that
  window open; it keeps the Mac awake until you press Ctrl+C. Source:
  https://ss64.com/mac/caffeinate.html (Apple's `man caffeinate`).
- Pause macOS automatic updates for the test days: System Settings > General
  > Software Update > Automatic updates, turn off "Install macOS updates".

1. Open an FTMO Free Trial on **cTrader** (spec 15.6: confirm a trial can be
   opened on cTrader; if only MetaTrader is offered, use another cTrader demo
   account and tell the developer).
2. Laptop settings for the test period: never sleep or hibernate, on battery or
   on power; closing the lid does nothing; keep it plugged in; pause automatic
   update restarts (spec 10.5).
3. Install the bot and check one instance exactly as in
   [`docs/test-records/WP08-adapter.md`](../docs/test-records/WP08-adapter.md),
   "Preparation" and steps 1 to 5. Fill in that record.
4. Add four more instances on **M15** charts of GBPUSD, USDJPY, AUDUSD and
   XAUUSD (gold) with the same parameters (only `HeartbeatUrl` differs, one
   monitor per symbol). The gold instance also has its own three values (see
   "Gold (XAUUSD)" below): use `deploy/params/xauusd.cbotset.example`.
5. Optional, one instance only: right-click it and choose **Start in external
   process**, then close the cTrader window; the cBot keeps running in a CLI
   process (source: https://help.ctrader.com/ctrader-cli/cbots/, "Launch from
   the Desktop Application"). This is the first real test of fact 2 above. If
   the log says the cBot needs a newer .NET, stop it, run it inside the app as
   in step 3, and tell the developer.
6. Run the laptop variant of the fault tests:
   [`docs/test-records/WP11-fault-injection.md`](../docs/test-records/WP11-fault-injection.md),
   column "Laptop". Test 2 cannot be done on a laptop; test 10 needs a Friday
   evening.
7. Move to the VPS when every applicable test passed and there was no position
   without a stop loss, no duplicate order and no recovery that needed your help
   (spec 10.5).

---

## Part B. Stage 2: the VPS (spec 10.2)

### 1. Rent the server

- A small Linux VPS in a commercial data centre **outside the United States**
  (FTMO does not accept a US location, spec 15.2).
- Operating system: **Ubuntu 24.04 LTS or 26.04 LTS, 64-bit** (both supported by
  Docker, see the Docker install page).
- Size: see "Decisions for the human" in the pull request; recommended start is
  2 vCPU, 4 GB RAM, 40 GB disk. Step 12.6 measures real use.
- If the provider lets you add an **SSH public key** when ordering, do step 3.1
  first and paste the key there. Then you never need a root password.

### 2. First login and system update

From the laptop. On a Mac, open **Terminal** (Applications > Utilities); `ssh`,
`scp` and `ssh-keygen` are built in. (Windows 10/11 also has an OpenSSH client
under Settings > Optional features > OpenSSH Client. Source:
https://learn.microsoft.com/windows-server/administration/openssh/openssh_install_firstuse):

```bash
ssh root@SERVER_IP
```

Source: same Microsoft page ("ssh user@host" form). Answer `yes` to the
host-key question the first time only.

```bash
apt update
apt upgrade
```

Source: https://ubuntu.com/blog/ubuntu-updates-best-practices-for-updating-your-instance

### 3. Your own user with SSH keys only

3.1 On the laptop, create a key pair (once; press Enter for the default file and
choose a passphrase):

```bash
ssh-keygen -t ed25519
```

Source: https://ubuntu.com/server/docs/how-to/security/openssh-server/

3.2 On the server (as root), create your user and give it administrator rights.
Use a user name of your choice instead of `zfvg`:

```bash
adduser zfvg
adduser zfvg sudo
```

Source: https://ubuntu.com/server/docs/how-to/security/user-management/

3.3 Put your public key on the server for that user. On a Mac, the simplest
way is one command in Terminal (it asks for the new user's password once):

```bash
ssh-copy-id zfvg@SERVER_IP
```

Then skip to 3.4. Otherwise (Windows), open the file
`C:\Users\YOU\.ssh\id_ed25519.pub` in Notepad (on a Mac:
`cat ~/.ssh/id_ed25519.pub` in Terminal) and copy its one line. On the server:

```bash
su - zfvg
mkdir -p ~/.ssh
nano ~/.ssh/authorized_keys
```

Paste the line, save with Ctrl+O, Enter, exit with Ctrl+X. Then:

```bash
chmod 700 ~/.ssh
chmod go-w ~/.ssh/authorized_keys
exit
```

Source: https://ubuntu.com/server/docs/how-to/security/openssh-server/ (the
`authorized_keys` file and `chmod go-w`). (On Linux or macOS you can use
`ssh-copy-id zfvg@SERVER_IP` instead, same page.)

3.4 **Open a second terminal** on the laptop and check that the key works. Keep
the first window open until step 3.6 is done.

```bash
ssh zfvg@SERVER_IP
sudo whoami
```

Expected: you log in without the account password (only the key passphrase) and
`sudo whoami` prints `root`.

3.5 Switch password login and root login off. As `zfvg`, create a small settings
file. Its name starts with `01-` so that it is read before any provider file in
the same folder (sshd uses the first value it finds; the main file includes
`/etc/ssh/sshd_config.d/*.conf` at the top):

```bash
sudo nano /etc/ssh/sshd_config.d/01-zfvg.conf
```

Content:

```
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
```

Sources: https://ubuntu.com/server/docs/how-to/security/openssh-server/
(drop-in folder, first value wins) and
https://man7.org/linux/man-pages/man5/sshd_config.5.html (the three settings
and their values).

3.6 Test the settings, then restart SSH:

```bash
sudo sshd -t
sudo systemctl restart ssh.service
```

Source: https://ubuntu.com/server/docs/how-to/security/openssh-server/
(`sshd -t` prints nothing when the file is valid).

From a **third** terminal, `ssh zfvg@SERVER_IP` must still work, and
`ssh root@SERVER_IP` must be refused. Only then close the first window.

3.7 Lock the root password:

```bash
sudo passwd -l root
```

Source: https://ubuntu.com/server/docs/how-to/security/user-management/

### 4. Firewall

Allow SSH first, then switch the firewall on. Nothing else is opened: the bot
only makes outgoing connections.

```bash
sudo ufw allow ssh
sudo ufw enable
sudo ufw status verbose
```

Sources: https://help.ubuntu.com/community/UFW (`allow ssh`, default deny
incoming) and https://ubuntu.com/server/docs/how-to/security/firewalls/
(`enable`, `status verbose`).

Expected: `Status: active`, `Default: deny (incoming), allow (outgoing)`, and a
rule allowing 22/tcp.

Note: ports published by Docker bypass ufw (Docker install page, firewall
note). The compose file publishes no port, so this does not apply. Never add a
`ports:` line to it.

### 5. Automatic security updates

```bash
sudo apt install unattended-upgrades
sudo nano /etc/apt/apt.conf.d/20auto-upgrades
```

The file must contain:

```
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
```

Source: https://ubuntu.com/server/docs/how-to/software/automatic-updates/
(results are logged in `/var/log/unattended-upgrades/`). Automatic reboot is
off by default (`Unattended-Upgrade::Automatic-Reboot` is `false`); keep it off.
You reboot by hand at the weekend when the runbook's weekly check says a reboot
is needed (see "Decisions for the human").

### 6. Time synchronisation

The bot uses broker server time, never the machine clock (spec 8.4), but a
correct clock keeps logs readable and TLS working.

```bash
timedatectl status
```

Source: https://ubuntu.com/server/docs/how-to/networking/timedatectl-and-timesyncd/

Expected: `System clock synchronized: yes` and `NTP service: active`. Ubuntu
25.10 and later (so 26.04) use chrony by default; there you can also run:

```bash
chronyc -N tracking
```

Source: https://ubuntu.com/server/docs/how-to/networking/chrony-client/

### 7. Docker

Follow "Install using the apt repository" on
https://docs.docker.com/engine/install/ubuntu/ exactly as written there
(remove conflicting packages, add Docker's key and `docker.sources`, then):

```bash
sudo apt install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo docker run hello-world
```

Source: https://docs.docker.com/engine/install/ubuntu/

Let your user run Docker without `sudo` (the `docker` group gives
administrator-level rights, so only your own user gets it), then log out and in
again:

```bash
sudo usermod -aG docker $USER
```

Source: https://docs.docker.com/engine/install/linux-postinstall/

**To check on first start:** Docker itself starts at boot.

```bash
systemctl is-enabled docker
```

Expected: `enabled`. If not, see "Configure Docker to start on boot" on the
post-installation page. Fault test 2 proves it.

### 8. Directory layout

Everything lives in `/opt/zfvg`, outside any git checkout:

```
/opt/zfvg/
  docker-compose.yml         copy of deploy/docker-compose.yml
  .env                       from deploy/.env.example        (chmod 600)
  secrets/ctrader-cli.pwd    cTrader ID password, one line   (chmod 600)
  algo/current/ZoneFvgBot.algo   the version that runs
  algo/previous/ZoneFvgBot.algo  the version before, for roll-back
  params/eurusd/bot.cbotset  from deploy/params/eurusd.cbotset.example (chmod 600)
  params/gbpusd/bot.cbotset
  params/usdjpy/bot.cbotset
  params/audusd/bot.cbotset
  params/xauusd/bot.cbotset  from deploy/params/xauusd.cbotset.example (gold)
  log-archive/               copies of container logs saved before updates
```

```bash
sudo mkdir -p /opt/zfvg
sudo chown $USER:$USER /opt/zfvg
cd /opt/zfvg
mkdir -p secrets algo/current algo/previous log-archive params/eurusd params/gbpusd params/usdjpy params/audusd params/xauusd
chmod 700 secrets params
```

Source: standard tools (`mkdir`, `chown`, `chmod`), see "Documentation used".
The parameter folders are mounted one per
container, read-only, so each container sees only its own `bot.cbotset`.

### 9. Copy the files from the laptop

On the laptop, in a folder that holds the repository files and the `.algo`
from the CI artifact `ZoneFvgBot-algo` of the commit you want to run (WP08
record, "Preparation", step 1):

```bash
scp deploy/docker-compose.yml deploy/.env.example zfvg@SERVER_IP:/opt/zfvg/
scp deploy/params/eurusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/eurusd/bot.cbotset
scp deploy/params/gbpusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/gbpusd/bot.cbotset
scp deploy/params/usdjpy.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/usdjpy/bot.cbotset
scp deploy/params/audusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/audusd/bot.cbotset
scp deploy/params/xauusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/xauusd/bot.cbotset
scp ZoneFvgBot.algo zfvg@SERVER_IP:/opt/zfvg/algo/current/ZoneFvgBot.algo
```

Source: https://man.openbsd.org/scp (`scp source ... [user@]host:[path]`).

Write down the commit of the `.algo` in the test record.

### 10. Secrets and parameters (on the server)

10.1 Account file:

```bash
cd /opt/zfvg
cp .env.example .env
chmod 600 .env
nano .env
```

Replace `your-ctrader-id` and `your-account-number`. Start with the **FTMO Free
Trial** account, never the Challenge account (spec 15.7).

10.2 Password file. Type the command below with a space in front of it, so the
shell does not keep it in its history, and put your password between the
quotes:

```bash
 printf '%s' 'YOUR-CTRADER-ID-PASSWORD' > /opt/zfvg/secrets/ctrader-cli.pwd
chmod 600 /opt/zfvg/secrets/ctrader-cli.pwd
```

Source: image README, "Quickstart" step 1 (one line, no trailing spaces,
`chmod 600`); https://help.ctrader.com/ctrader-cli/setup/ (keep it out of
version control and restrict read access). Spec 10.2: readable only by the
service user.

10.3 Parameter files. In each `params/<symbol>/bot.cbotset`, replace every
`CHANGE_ME...` value:

| Parameter | What to put |
|---|---|
| `RiskEpochDate` | The first day of this account, `yyyy-MM-dd`. It means 00:00 Prague time that day (architecture section 10). Same value in all five files. Runbook: "Changing RiskEpochDate" |
| `NewsFeedUrl` | The Forex Factory weekly calendar export address (JSON) you used on the laptop. Same in all five files |
| `TelegramBotToken`, `TelegramChatId` | From your Telegram bot (spec 11). Same in all five files |
| `HeartbeatUrl` | One check per symbol at your heartbeat-monitoring service, so an alert names the symbol that stopped |
| `ProfitTargetPercent` | 10 in the Challenge, 5 in the Verification |

Leave every other value at the spec default. `MaxSpreadPips` is 1.5 for the
four forex symbols until the broker conditions (spec 10.4) say otherwise. A value
left as `CHANGE_ME...` makes the bot refuse to start (`RiskEpochDate`) or fail
safely (no calendar means no entries; failed alerts are logged).

```bash
chmod 600 /opt/zfvg/params/*/bot.cbotset
```

### Gold (XAUUSD)

Gold runs as a fifth instance (spec v0.9, owner decision D of 2026-11-17,
requirement PLT-010): its own container `xauusd`, parameter file
`params/xauusd/bot.cbotset`, storage and log volumes and heartbeat check. Its
parameter file is a copy of the forex file with exactly these differences:

| Parameter | Forex | Gold | Why |
|---|---|---|---|
| `MaxSpreadPips` | 1.5 | 40.0 | Gold's pip is 0.01 USD; a normal gold spread is far above 1.5 such pips |
| `MinStopPips` | 12.0 | 800.0 | 8.00 USD per ounce |
| `MaxVolumeUnits` | 500000.0 | 200.0 | Gold volume is in ounces |
| `HeartbeatUrl` | one check per symbol | its own check | So an alert names gold |

No other parameter is in pips; the ATR and R multiples scale by themselves.
The cBot's default values stay the forex values, so a gold instance started
without its parameter file would use the forex limits: always load
`xauusd.cbotset`.

Before the gold instance trades on the demo account, open the XAUUSD symbol
information in cTrader and compare it with the adapter's start line
(`symbol XAUUSD ... pipSize ... digits ... tickSize ... lotSize ...
commission ... volumeMin ... step ... max ... sessionsAsReported ...`):
digits, tick size, pip size (0.01 is assumed by the values above), lot size
(ounces per lot), commission and its type, volume minimum and step, and the
trading sessions with the daily break. Write them into
`docs/test-records/WP11-fault-injection.md` with the PLT-010 check.

### 11. Check the compose file

```bash
cd /opt/zfvg
docker compose config --quiet
```

Source: https://docs.docker.com/reference/cli/docker/compose/config/
Expected: no output. An error names the line to fix.

### 12. First start (FTMO Free Trial only)

Do this on a weekday during market hours. Fill in "First-start checks" in the
fault-injection record as you go.

12.1 Pull the pinned image and write down its digest:

```bash
docker compose pull
docker image inspect --format '{{index .RepoDigests 0}}' ghcr.io/spotware/ctrader-console:5.9.11
```

Sources: https://docs.docker.com/reference/cli/docker/compose/pull/ ;
https://docs.docker.com/reference/cli/docker/image/inspect/ ; image README
("Pin a version tag in production, or a digest").

12.2 The CLI can read the `.algo` (no account needed). This is the .NET check:

```bash
docker compose run --rm --no-deps eurusd metadata /mnt/algo/ZoneFvgBot.algo
```

Sources: https://docs.docker.com/reference/cli/docker/compose/run/ (the words
after the service name replace its command); `metadata`:
https://help.ctrader.com/ctrader-cli/cbots/ ("Read cBot Metadata").
Expected: the parameter list of ZoneFvgBot (BiasTimeFrame ... DrawObjects).
If it says the cBot is built for an older .NET: stop here and tell the
developer.

12.3 The account and the symbol names:

```bash
docker compose run --rm --no-deps eurusd accounts --environment-variables
docker compose run --rm --no-deps eurusd symbols --environment-variables
```

Sources: image README, "What each command accepts" (`accounts`/`symbols` read
`CTID`, `PWD_FILE`, `ACCOUNT` from the environment); command reference
`accounts`, `symbols`. Expected: your trial account is listed; the symbol list
contains `EURUSD`, `GBPUSD`, `USDJPY`, `AUDUSD` and `XAUUSD` spelled exactly like that.
If FTMO uses other names (for example with a suffix), change `SYMBOL` in the
compose file and tell the developer.

12.4 Start **one** instance and watch it:

```bash
docker compose up -d eurusd
docker compose logs -f eurusd
```

Sources: https://docs.docker.com/reference/cli/docker/compose/up/ ;
https://docs.docker.com/reference/cli/docker/compose/logs/
Press Ctrl+C to stop watching (the bot keeps running). Expected, as in WP08
record step 1: `ZFVG adapter version ...`, `symbol EURUSD`, `backtest no`; a
core `start` line; a recovery report; state Running (or Blocked with a reason);
a Telegram "Started" message; no token or address visible in the log.

12.5 Local storage location and persistence (fact 6):

```bash
sudo find "$(docker volume inspect --format '{{ .Mountpoint }}' zfvg_eurusd-data)" -type d -name LocalStorage
```

Sources: https://docs.docker.com/reference/cli/docker/volume/inspect/ ; folder
name from https://help.ctrader.com/ctrader-algo/guides/local-storage/
Expected: one `.../cAlgo/LocalStorage` folder. Under it,
`cBots/ZoneFvgBot/<id>/`. Write down `<id>`. Then:

```bash
docker compose restart eurusd
```

Source: https://docs.docker.com/reference/cli/docker/compose/restart/
Wait two minutes and list `cBots/ZoneFvgBot/` again. Expected: the **same**
`<id>`, no new one; the second start's log shows the calendar taken from the
cache, without a new download within the hour (spec 6.2). If a new `<id>`
appears at every start, stop and tell the developer (fact 6).

12.6 Start the other three and measure resources:

```bash
docker compose up -d
docker compose ps
docker stats --no-stream
```

Sources: compose `up`, `ps` (https://docs.docker.com/reference/cli/docker/compose/ps/);
https://docs.docker.com/reference/cli/docker/container/stats/
Expected: five containers `zfvg-eurusd-1`, `zfvg-gbpusd-1`, `zfvg-usdjpy-1`,
`zfvg-audusd-1`, `zfvg-xauusd-1`, status `Up`. Write down memory use per container.

12.7 Run the fault-injection tests (record, VPS column), then the four-week soak
test (spec 12.3). Only after both, think about the Challenge (spec 15.6).

### 13. Everyday commands

| Task | Command | Source |
|---|---|---|
| Status of the five containers | `docker compose ps` | compose `ps` |
| Last 200 log lines of one symbol | `docker compose logs --tail 200 eurusd` | compose `logs` |
| Log lines of the last 24 hours with times | `docker compose logs --since 24h --timestamps eurusd` | compose `logs` |
| Stop one symbol (no new trades; stops stay at the broker) | `docker compose stop eurusd` | https://docs.docker.com/reference/cli/docker/compose/stop/ |
| Start it again | `docker compose start eurusd` | https://docs.docker.com/reference/cli/docker/compose/start/ |
| Restart after editing its `bot.cbotset` | `docker compose restart eurusd` | compose `restart` |
| Disk use | `df -h /` and `docker system df` | standard tool; https://docs.docker.com/reference/cli/docker/system/df/ |

Run every `docker compose` command from `/opt/zfvg` (`cd /opt/zfvg` first).

### 14. Save the logs before re-creating containers

`docker compose logs` keeps working across restarts and reboots, but the log
of a container is deleted when the container is re-created (an update, a
roll-back, `docker compose down`). Save it first:

```bash
cd /opt/zfvg
docker compose logs --no-color --timestamps > log-archive/all-$(date -u +%Y%m%dT%H%MZ).log
```

Source: compose `logs`. The per-symbol `*-logs` volumes (mounted at `/logs` in
each container) are reserved for log files the CLI may write itself; whether it
does is noted at first start. They are kept across re-creation.

### 15. Update the bot to a new version (spec 10.2)

A new `.algo` is deployed **only when the bot is flat** (no bot position and no
pending bot order on any symbol), **or** after the adoption test for exactly
this pair of versions (old to new) has passed on a trial account (fault test 11
procedure, "Adoption test" in the record).

1. Read the release notes in the pull request. If they change a parameter, edit
   the five `bot.cbotset` files in step 6, not before.
2. Prefer Saturday. Check in the cTrader app that there is no bot position and
   no bot pending order, or that the adoption test for this version pair is
   signed.
3. Save the logs (section 14).
4. Keep the running version for roll-back:

   ```bash
   cd /opt/zfvg
   cp algo/current/ZoneFvgBot.algo algo/previous/ZoneFvgBot.algo
   ```

5. Copy the new `.algo` from the laptop to `algo/current/ZoneFvgBot.algo`
   (step 9).
6. Re-create the containers so they load the new file:

   ```bash
   docker compose up -d --force-recreate
   docker compose logs -f
   ```

   Source: compose `up` (`--force-recreate`).
7. Expected: five start lines with the **new** version, five recovery reports,
   five Telegram "Started" messages. If anything is unexpected, roll back
   (section 16).

### 16. Roll back

1. Save the logs (section 14).
2. Put the previous version back and re-create:

   ```bash
   cd /opt/zfvg
   cp algo/previous/ZoneFvgBot.algo algo/current/ZoneFvgBot.algo
   docker compose up -d --force-recreate
   ```

3. Check the start lines show the old version. A position opened by the newer
   version is adopted only if the older version can read it; if the recovery
   report lists it as a stranger, it keeps its stop and target at the broker
   (spec 8.3, step 6) and is reported. If unsure, close it in the cTrader app.

### 17. Change the cTrader CLI image version

Spec 8.6 and 10.2: only after all fault tests have passed on demo with the new
version.

1. On a **trial account** server (or with the five containers pointing at a
   trial account), change the tag in `image:` of `docker-compose.yml`, never to
   `latest`.
2. `docker compose pull`, save the logs, `docker compose up -d --force-recreate`.
3. Run the fault-injection record again with the new version.
4. Only then change the live server the same way. To roll back, put the old tag
   back and repeat step 2.

### 18. Reboot the server

Only at the weekend when the bot is flat, or as fault test 2:

```bash
pro system reboot-required
sudo reboot
```

Source: https://ubuntu.com/blog/ubuntu-updates-best-practices-for-updating-your-instance
(`pro system reboot-required`); `reboot`: https://man7.org/linux/man-pages/man8/reboot.8.html.
After the reboot, `docker compose ps` must show
the five containers `Up` without any action from you.
