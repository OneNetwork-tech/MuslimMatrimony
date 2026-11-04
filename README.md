# Nikah Noor (MuslimMatrimony)

Nikah Noor is a matrimonial platform for Muslims seeking marriage. Members create a profile, search for members of the opposite gender, and show interest. When the interest is mutual, the two members become a match. Matches can message each other and see each other's wali (guardian) details. Photos are moderated, and members control who sees them.

It is built with .NET 10 (LTS), ASP.NET Core MVC, ASP.NET Core Identity and PostgreSQL. The UI is available in English and Swedish.

## Features

| Area | What you get |
|---|---|
| **Accounts** | Registration requires age 18+, explicit GDPR Art. 9 consent for religious data, and acceptance of the terms. Email confirmation is required before you can sign in. Lockout after 5 failed attempts, password reset, two-factor authentication (Identity) |
| **Profiles** | Marital status, sect, religious practice, prayer, education, profession, location, children, relocation, preferred age range, optional wali details. Profiles can be paused (hidden from search) |
| **Search** | Age, country, city, marital status, sect, practice, "with photo", "willing to relocate", sorting, pagination. Defaults to your preferred age range |
| **Interests and matches** | "I'm interested" becomes a match when it is mutual. You get an email notification on a match. Interests can be withdrawn, which ends the match |
| **Messaging** | Only between mutual matches. Unread counts, read receipts, history kept after a match ends (but no new messages) |
| **Photos** | Up to 6 per member (JPEG/PNG/WebP, max 5 MB, checked by content). EXIF/GPS/XMP metadata is stripped on upload. Every photo is approved by a moderator. Visibility is matches only (default) or all members. Served only through an authorised endpoint |
| **Safety** | Block (hides both members from each other, including conversations) and confidential reports |
| **Moderation** | Admin page for photo review, open reports, and suspending or reinstating members. Suspension signs the member out within a minute and hides them from everyone |
| **GDPR** | Download all your data (ZIP with `data.json` and your photos) and delete your account (removes the profile, photos, interests and messages, including the files on disk) |

## Layout

```
MuslimMatrimony.Web     The app: Controllers, Services (matching rules, messaging, photos, GDPR, moderation),
                        Views, Identity page overrides (Areas/Identity), EF Core model + migrations (Data)
MuslimMatrimony.Tests   Unit tests (image sanitising, age) + integration tests against a real PostgreSQL
tools/                  extract_strings.py lists the strings that need a Swedish translation
```

All visibility rules (opposite gender, confirmed and not suspended, not blocked, hidden profiles only for matches, photo visibility) live in `Services/MatchService.cs`, so search, profile pages, photos and messaging all apply the same ones. Anything a member is not allowed to see returns `404`, so IDs can't be probed.

## Getting started (Docker)

```bash
cp .env.example .env     # fill in POSTGRES_PASSWORD and the Email__* settings
docker compose up -d --build
```

The app runs on <http://localhost:8080> and applies database migrations on startup. In production, put a TLS-terminating reverse proxy in front of it. Cookies are `Secure`-only outside Development.

## Deploy to your own server (CI/CD with Docker)

Every push to GitHub runs the tests and, if they pass, publishes the Docker image to GitHub Container Registry:

| Tag | Points to |
|---|---|
| `latest` | the newest build of the default branch |
| `<branch>` | the newest build of a branch, e.g. `claude-charming-noether-lhy664` (`/` becomes `-`) |
| `sha-<7 chars>` | one exact commit |
| `1.2.3` | a `v1.2.3` git tag |

Image: `ghcr.io/onenetwork-tech/muslimmatrimony`.

The server needs nothing except Docker with the Compose plugin. Nothing is built there.

**One-time setup on the server**

1. Log in to the registry with a GitHub token that has `read:packages`. Packages are private unless you make them public under the repository's *Packages*.

   ```bash
   docker login ghcr.io -u <your-github-username>
   ```

2. Copy the deploy files from this repository and create the settings file:

   ```bash
   mkdir -p ~/apps/muslimmatrimony && cd ~/apps/muslimmatrimony
   # copy deploy/docker-compose.yml, deploy/deploy.sh and deploy/.env.example here, then:
   cp .env.example .env
   ```

3. Fill in `.env`. Generate the secrets with `openssl` as the comments say, and set `IMAGE_TAG` to `latest` or to a branch tag.

**Deploy or update**

```bash
./deploy.sh                # pull IMAGE_TAG from .env and restart
./deploy.sh sha-1a2b3c4    # roll forward or back to one exact build
```

`deploy.sh` pulls the image, restarts the stack, waits until `/health` reports healthy, and prints the logs if it doesn't. Data is kept in Docker volumes across updates. To start from scratch, run `docker compose down -v`, which deletes all data.

**Plain HTTP on a LAN.** The app is HTTPS-only in production by default. When you test it as `http://<server-ip>:<port>`, keep `Security__HttpsOnly=false` in `.env` as the example has it, or login won't work. Once a TLS reverse proxy (Caddy, Traefik, nginx) is in front, switch it back.

**Automatic deploys (optional).** CI can deploy every push of one branch to your server:

1. On the server, install a [self-hosted GitHub Actions runner](https://docs.github.com/actions/hosting-your-own-runners) for this repository and give it the label `local-server`. The runner's user must be allowed to run `docker`.
2. Create `~/apps/muslimmatrimony/.env` on the server once, as above.
3. In the repository's *Settings → Secrets and variables → Actions → Variables*, set `LOCAL_DEPLOY_BRANCH` to the branch to deploy, for example `main`. Set `LOCAL_DEPLOY_DIR` too if you use a different folder.

The `deploy-local` job then runs `deploy.sh` with the exact image CI just built (`sha-…`). It is skipped when `LOCAL_DEPLOY_BRANCH` isn't set.

**Running all four apps on one server.** The deploy files use different ports, so SalmanPlex, BoklyAI, RentalHive and Nikah Noor can run side by side:

| App | Port(s) | Health check |
|---|---|---|
| SalmanPlex | 3001 | `/api/health` |
| BoklyAI | 3002 | `/api/health` |
| RentalHive | 8081 (web), 8082 (API) | `/health` |
| Nikah Noor | 8083 | `/health` |

## Development

You need the .NET 10 SDK and PostgreSQL 14 or newer.

```bash
export ConnectionStrings__MuslimMatrimony="Host=localhost;Database=muslimmatrimony;Username=muslimmatrimony;Password=…"
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project MuslimMatrimony.Web
```

If `Email:Host` is not set, emails are not sent. In Development they are written to the log, including the confirmation link, so you can register locally without SMTP.

**Moderators:** list their email addresses in `Admin__Emails__0`, `Admin__Emails__1`, and so on. On startup, confirmed accounts with those addresses get the `Admin` role.

To create a new migration (the app's own startup is used, so set the connection string):

```bash
ConnectionStrings__MuslimMatrimony="Host=localhost;Database=muslimmatrimony;Username=…;Password=…" \
  dotnet ef migrations add <Name> --project MuslimMatrimony.Web --output-dir Data/Migrations
```

### Tests

```bash
TEST_DATABASE_URL="Host=localhost;Database=muslimmatrimony_test;Username=muslimmatrimony;Password=…" dotnet test
```

There are 35 tests:

- **Unit tests** for metadata stripping (JPEG, PNG, WebP), rejection of non-images and truncated files, and age calculation.
- **A localisation test** that fails if any user-facing string lacks a Swedish translation.
- **Integration tests** that drive the real app over HTTP, with cookies and antiforgery tokens:
  - registration rules
  - search visibility
  - matching and messaging
  - photo moderation and visibility
  - reports and suspensions
  - GDPR export and deletion
  - security headers, antiforgery, and the language switch

The test database's schema is dropped and recreated on every run, so use a dedicated database. CI also checks that the migrations match the model, builds the Docker image, and fails on high or critical vulnerable packages.

## Security and privacy

- **Special-category data:** religion is processed on explicit consent only. The consent time and version (`Consent.CurrentVersion`) are stored with the account and included in the export.
- **Accounts:** ASP.NET Core Identity with confirmed email. Passwords must be at least 10 characters (length rather than composition rules). Lockout after 5 failures, rate-limited credential endpoints, and security-stamp checks every minute, so suspensions and password changes end other sessions quickly. Registration doesn't reveal whether an email is already in use.
- **Photos:** checked by content, metadata stripped without re-encoding, stored outside `wwwroot` under random names, and served with `nosniff` and `private` caching. Rejected photos are deleted.
- **Web:**
  - a strict Content-Security-Policy (`'self'` only, no inline scripts or styles)
  - no CDNs or Google Fonts
  - `X-Frame-Options: DENY`, `Referrer-Policy` and `Permissions-Policy` headers
  - HSTS in production
  - antiforgery validation on every POST
  - local-only redirects
  - all user content is HTML-encoded
- **Data minimisation:** other members see only a first name, an age (never the date of birth) and the profile. Email addresses are never shown to other members, and match notification emails contain no profile details.

## Configuration

| Setting | Purpose |
|---|---|
| `ConnectionStrings__MuslimMatrimony` | PostgreSQL connection string (required) |
| `Email__Host` / `Port` / `Security` / `Username` / `Password` / `From` / `FromName` | SMTP for confirmation, password reset and match emails. `Security` is `StartTls`, `SslOnConnect` or `Auto` |
| `Admin__Emails__N` | Accounts that get the moderator role |
| `Storage__PhotoDirectory` | Where photos are stored (a volume in Docker) |
| `DataProtection__KeysPath` | Cookie encryption keys; must persist across restarts and be shared by all instances |
| `ForwardedHeaders__KnownProxies__N` | Reverse-proxy IPs to trust for client IP and scheme |
| `Database__MigrateOnStartup` | Apply migrations automatically (default `true`) |
| `RateLimiting__Enabled` | Default `true`; the tests turn it off |

## Not included yet

- Identity verification (for example BankID) and verified badges.
- Real-time chat (messages appear when the page is reloaded).
- Paid memberships.
- Swedish text on the built-in Identity pages (login, password reset, account settings). Those pages are in English; the rest of the site is translated.
- Retention of reports after the reported member deletes their account. Deletion currently cascades to reports about them.
