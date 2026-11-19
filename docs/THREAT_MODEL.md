# Threat Model

A short threat model for the Araç Kiralama desktop application. The application stores accounts with passwords,
e-mail addresses and phone numbers, the company's cars with their plates, and the rentals of every account with
their days and prices. User names, e-mail addresses and phone numbers, and the rentals linked to them, are
personal data under the Turkish Personal Data Protection Law (KVKK, Law No. 6698). This document lists technical
measures only; it is not a legal assessment.

## Scope and assets

- **Application:** a Windows Forms client with customer and admin sign-in that talks directly to SQL Server and
  downloads car pictures from the web.
- **Assets:**
  - Accounts: user names, e-mail addresses, phone numbers, roles, passwords and their stored hashes; sign-in
    counters and lockout times.
  - Cars with plates and daily prices; rentals with pick-up and return days, prices and status.
  - The maintenance and damage log of the cars (days, descriptions, costs) and the revenue it implies.
  - Exported files: revenue reports (CSV) and rental contracts (HTML) saved where the user chooses.
  - Setup keys issued by the database owner (`KurulumAnahtarlari`, stored as SHA-256 hashes).
  - The connection string (server, database, and possibly SQL credentials).
  - Log files under `%LOCALAPPDATA%\AracKiralama\logs`.
  - Database backups made by the operator (`.bak` files).
- **Trust boundaries:** the Windows user session that runs the application; the network link to SQL Server; the
  network link to picture hosts; the local file system (settings, logs, backups, exported files); the boundary
  between a signed-in account and the data of other accounts; the boundary between the application's SQL login and
  the database owner.

## Assumptions

- The application runs on trusted workstations; people sign in on them with their own accounts.
- Every instance connects with the same SQL Server login, so the database itself does not tell accounts apart:
  roles and ownership are enforced by the application.
- The database owner (db_owner or sysadmin) is a different, more trusted principal than the application's login,
  and is the only one who creates schema and setup keys.
- The operator is the data controller and is responsible for consent, retention, deletion requests and the
  security of the server, its logins and its backups.
- A new database contains no account. Demo mode (`ARAC_DEMO=1` or `Seed:Demo=true`) adds only fictional cars, and
  only to a completely empty database; it never creates an account.
- The clocks of the workstations may be wrong; their time zone is set right.

## Identity and authentication

| Threat | Mitigation | Status |
|---|---|---|
| Passwords read from the database or a backup | PBKDF2-HMAC-SHA256 with 600,000 iterations and a 16 byte random salt per password; compared in constant time | Mitigated |
| Plain text passwords of the original application | The legacy upgrade marks them as legacy plain text in their own format column; the first successful sign-in replaces each with a PBKDF2 hash in the same conditional update | Partly mitigated (see residual risks) |
| A stored value read in the wrong format | The format comes from the format column, never from the shape of the value | Mitigated |
| Weak new passwords | Every new password (sign-up, setup, password change, admin reset) goes through the same validator: 8 to 64 characters | Mitigated (length policy only) |
| Online password guessing | 5 failed attempts lock the account for 15 minutes; the counter is increased in one atomic `UPDATE`, attempts during a lock are neither counted nor extend it, and a success is written only while the account is still unlocked and unchanged. A wrong current password in the password change counts as a failed sign-in. Concurrent failures are all counted (tested on SQL Server) | Mitigated |
| A workstation clock that is ahead or behind ending or extending a lock | Locks are set and checked with the database's clock (`SYSUTCDATETIME`); tested with clocks ahead and behind on SQL Server | Mitigated |
| Account enumeration through sign-in answers or timing | Unknown accounts, wrong passwords and locked accounts get the same message, and each costs one key derivation | Mitigated |
| Account enumeration through sign-up | The sign-up form names the taken values, as the original form did; anyone at the sign-up screen can learn whether a user name, e-mail address or phone number is registered | Accepted |
| Lockout used to block a known account (denial of service) | The lock ends by itself after 15 minutes; no permanent lock exists, and an admin's password reset lifts it | Accepted |
| Accounts with published passwords in a default installation | The migrations insert no rows; demo mode adds cars only | Mitigated |

## Roles, sessions and ownership

| Threat | Mitigation | Status |
|---|---|---|
| A customer managing cars, accounts or every rental | These calls require the admin role; a customer's session is refused before any repository is asked and the refusal is audited as `access.denied` | Mitigated |
| A stale session: an account deleted or demoted while it is signed in | The session's role only decides what it may ask for. Every operation reads the account's role from the database first (`AccessGuard`), and every privileged write takes the acting account and checks its stored role again inside its own statement or transaction; when the account is gone or lost the role, nothing is written and the call is denied. Tested on SQLite and on SQL Server, also for an admin demoted during a write | Mitigated |
| Self-promotion to admin | Sign-up always creates the user role; a customer's session cannot change roles; the first admin is created only on an empty database, or with a setup key from the database owner while no admin exists, atomically (two setups at once create one admin, tested on SQL Server) | Mitigated |
| Losing the last admin | The last admin cannot be demoted, also when two admins demote each other at the same time (tested on SQL Server); admins cannot be deleted | Mitigated |
| IDOR: ending or listing another account's rental | A customer's rentals are read and ended only with the session's own account id in the statement; another account's rental, a missing one and one already ended get the same answer and leave the same log entries, so rental ids cannot be probed. Admins may end any rental | Mitigated |
| An old session revived when a change is undone (an admin demoted and promoted again, a password reset) | Every account has a session version (`OturumSurumu`), raised in the same statement as every role change and every password an admin sets. A session carries the version it was issued with; `AccessGuard` refuses it once the stored version differs, and every write checks the version, the role and the account again inside its transaction (HOLDLOCK on SQL Server). A sign-in verified before such a change cannot be recorded. Tested on SQLite and on SQL Server, also for an admin demoted and promoted again while the write waits for the car's lock | Mitigated |
| IDOR: another account's contract | A customer's contract is read with the session's own account id in the statement; another account's rental and a missing one get the same answer. The calendar refuses an unlisted car to customers like a missing one | Mitigated |

## Bookings and locks

| Threat | Mitigation | Status |
|---|---|---|
| A car booked twice for the same day | A booking is a half-open day range. It takes an exclusive `sp_getapplock` lock per car and checks for shared days with the car's active rentals in the same transaction; a filtered unique index on the car and the pick-up day of active rentals backs it up. Races of two and of many customers for the same days, and adjacent ranges at once, are tested on SQL Server | Mitigated |
| A booking in the past from a workstation whose clock is behind | The first allowed day is today by the database's clock, converted to the workstation's time zone; a quote prepared before midnight is checked again when it is booked (tested on SQL Server and SQLite) | Mitigated |
| A rental under way cancelled instead of returned | Whether ending a rental cancels it (before its pick-up day) or returns it (from its pick-up day on) is decided by the database's clock inside the write | Mitigated |
| A booking at a price the customer did not see | The quote's daily price is compared with the stored price inside the transaction; a changed price refuses the booking. The total is worked out again from the days and the settings | Mitigated |
| A rental ended twice | Ending updates only an active rental, so two concurrent requests end it once (tested on SQL Server). Rentals are never deleted | Mitigated |
| A booking on maintenance days, or maintenance on rented days | Maintenance writes take the same per-car lock as bookings, cancellations, returns and car changes, check the acting admin and the car's active rentals inside the transaction, and a booking checks the car's active maintenance records under the lock. Races of a booking with a maintenance record, a booking that commits while maintenance waits and a cancellation while a booking waits are tested on SQL Server | Mitigated |
| A stale form writing over another admin's change | Every maintenance record has a version, raised by each change; an update or cancellation names the version its form loaded and the conditional UPDATE matches no row otherwise. The form tells the admin and loads the record again; two admins saving one version at once store one change (tested on SQL Server) | Mitigated |
| Stored times from a wrong workstation clock | Maintenance records are stamped and their day limits checked with the database's UTC clock; the calendar, the report's creation time and the contract's issue time use it too | Mitigated |
| A held lock making operations hang | The lock wait is bounded (30 seconds) and shorter than the command timeout (90 seconds); a request that cannot get the car's lock ends with a "busy" message instead of a command timeout | Mitigated |
| Totals overflowing | Totals are computed in `decimal` and refused above the largest amount the money column can hold; rental length, horizon and discount are limited by validated settings; check constraints guard prices, mileage, dates and status | Mitigated |

## Exports

| Threat | Mitigation | Status |
|---|---|---|
| CSV formula injection through car names, plates or descriptions | Every CSV field is quoted, inner quotes are doubled, and a value starting with `=`, `+`, `-`, `@`, a tab, a carriage return or a line feed (also after spaces) gets a leading `'`, so spreadsheets read it as text (tested) | Mitigated |
| Script injection through a contract | Every value of the contract page is HTML encoded; the page has no script, loads no resource and carries a Content-Security-Policy (`default-src 'none'`, its own style block pinned by hash, `img-src 'none'`, `base-uri 'none'`, `form-action 'none'`) and `no-referrer` (tested) | Mitigated |
| Data leaking through exported files | A contract shows the rental, the car and the renter's e-mail address and phone, nothing else of the account: no sign-in name (half of the credentials, and a contract is printed and handed out), id, password or session data. The report holds no personal data. Files are written to a temporary file and moved over the target only when complete. Whoever receives a contract receives the renter's contact data; share it as such | Documented |
| Revenue data seen by customers | The report is admin only and is refused before the database is read | Mitigated |

## Car pictures

| Threat | Mitigation | Status |
|---|---|---|
| Server-side request forgery from every customer's computer: a picture address pointing to localhost, the local network or a cloud metadata address | Picture addresses must be absolute http or https addresses of at most 255 characters when an admin saves them. When a picture is shown, the address must carry no user information and every address the host name resolves to must be public (no loopback, private, link-local, shared, multicast, broadcast or reserved address, also not inside an IPv6 transition address), and the connection goes to the address that was checked. Redirects are not followed | Mitigated |
| Local files or network shares opened through a picture address | Only http and https are fetched; anything else shows the application's own placeholder without opening it | Mitigated |
| Large or malicious images | At most 2 MB of an image content type, at most 4096 pixels per side, within 10 seconds, at most 3 downloads at once; no proxy, cookies or credentials are sent | Mitigated |
| A picture host learning which customers look at which car | Pictures are fetched by the customer's computer, so the host sees its address and the time | Accepted |

## Setup key and the accepted limit

| Threat | Mitigation | Status |
|---|---|---|
| A client creating its own authority to become the first admin | The application has no way to create a key. Only the database owner runs `docs/issue-setup-key.sql`, which takes 16 random bytes, stores only the SHA-256 hash and shows the key once; the script refuses when an admin exists | Mitigated |
| The application's login forging, extending or deleting keys | `docs/grant-app-login.sql` grants `SELECT` on the key table and `UPDATE` only of `Tuketildi` and `TuketimTarihi`, and denies `INSERT`, `DELETE` and `UPDATE` of the id, hash, issue time and expiry; tested with that login on SQL Server | Mitigated |
| Guessing a key | 128-bit keys; every refused key is followed by a fixed 1 second pause; stored keys are compared in constant time. Wrong attempts are not counted, so nobody can revoke the founder's key by guessing | Mitigated |
| Reusing a key or using it after its 24 hours | A key is used up by a conditional `UPDATE` in the same transaction that writes the admin, and its expiry is decided by the database's clock; a failed write rolls the use back; unused keys are refused once an admin exists | Mitigated |
| Keys in logs or settings | The key is never logged or stored by the application; `ARAC_SETUP_KEY` only prefills the field and a malformed value is not shown | Mitigated |
| **Accepted limit:** whoever holds the application login's credentials | That login may read and write the account, car, rental and maintenance tables (deleting accounts and cars, never rentals or maintenance records), read the setup key hashes and mark keys used. Anyone with the connection string can therefore read all personal data, change rows directly, raise an account's role in the table, or spend an unused key without creating an admin; they cannot create or extend a key. Protect the connection string and grant the login nothing more than `docs/grant-app-login.sql` | Accepted |

## Logging

| Threat | Mitigation | Status |
|---|---|---|
| Personal data written to log files | Error entries contain only exception types, HResult codes and method names, without messages or file paths; refused operations leave no entry; audit entries are a fixed code and a role, with no account id, user name, e-mail address, phone number, password, setup key, car, plate, date or amount. Tests run whole sessions on SQLite and check the files | Mitigated |
| Account existence or lock state disclosed through the log | Every refused sign-in, from an empty field to a locked account, writes the same `auth.sign_in.failed unknown` entry; a new account and a sign-up with taken values write the same `auth.account.registration_submitted user` entry; a foreign, a missing and an ended rental leave the same entries. Tests compare the files of each outcome | Mitigated |
| Log files read by other Windows users | Logs are under the user's own local application data folder | Mitigated by OS permissions |
| Log files read by another application account on the same Windows account | The files hold only anonymous codes, roles and exception types; counts and times of events remain visible | Mitigated (residual) |
| Log files growing without bound | Daily rolling files; files dated more than 30 days ago and any beyond the newest 30 are deleted. Tests create dated files and check which remain | Mitigated |
| Personal data on the screen | Users see fixed Turkish messages, never raw exception text; the admin's account list masks e-mail addresses and phone numbers | Mitigated |

## Backups

| Threat | Mitigation | Status |
|---|---|---|
| Backups exposing personal data and legacy plain text passwords | `*.bak`, `*.mdf` and `*.ldf` are git-ignored. Backups taken before the upgrade, or while legacy accounts remain, contain plain text passwords: encrypt them (SQL Server backup encryption or an encrypted volume), restrict access and delete old ones | Documented |
| Legacy upgrade altering or exposing existing rows | The upgrade runs in one transaction under a checked `sp_getapplock` lock and refuses before any change when rows have no certain meaning; the refusal names counts only, never row contents | Mitigated |

## Connection security

| Threat | Mitigation | Status |
|---|---|---|
| Connection string leaked through the repository | `appsettings.json` is git-ignored; user secrets and the `ARAC_CONNECTION` environment variable are supported; only `appsettings.example.json` is committed, without a real server name or credentials | Mitigated |
| Interception of the database connection | `TrustServerCertificate=True` in the example is for a local development server only; for any other server use `Encrypt=True` with a certificate the client trusts and leave `TrustServerCertificate` off | Documented |
| SQL injection | All access goes through EF Core with parameters; the raw statements are constant lock, clock and schema statements built without user input | Mitigated |
| Excessive rights of the application's login | Use `docs/grant-app-login.sql`: no DDL, no deleted rentals or maintenance records, restricted key table rights; run it again after a migration that adds a table. Do the first start and every migration with the owner's connection; prefer Windows authentication | Documented |

## KVKK

- Personal data (user name, e-mail address, phone number, rentals) is kept only in the configured SQL Server
  database and never written to log files; passwords are never shown or read back.
- The application has no retention feature; ended rentals stay as history and accounts with rentals cannot be
  deleted from the application. The operator deletes data in the database when required.

## Residual risks

- Accounts migrated from the original application keep their plain text password until their next successful
  sign-in; an admin can force the change by setting a new password for those accounts.
- Names, e-mail addresses and phone numbers of original accounts that today's rules refuse are kept as long as
  the admin's form sends them unchanged.
- The sign-up form tells whether a user name, e-mail address or phone number is taken (accepted above).
- The booking screen and the calendar show every customer the days a car is taken or in maintenance, though not by
  whom or why in detail.
- On a Windows account shared by several application users, each of them can read when sign-ins, sign-ups and
  bookings happened from the log file, though not by whom.

## Out of scope

- Payments (the rental screen takes none), network services, web access, multi-factor authentication, encryption
  of data at rest inside the application, automated backups and retention policies, and a per-user audit trail of
  who read which record.
