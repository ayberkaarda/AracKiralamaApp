# Changelog

🇬🇧 **English** · 🇹🇷 [Türkçe](CHANGELOG.tr.md)

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Availability calendar of a car: every day of a month free, booked, in maintenance or past by the database's
  today, with the state written in each cell (customers for listed cars, admins for any car).
- Maintenance and damage log for admins (migration `AddMaintenanceLog`, table `AracBakim`): records block the car's
  days, cannot take days of an active rental, are cancelled instead of deleted and are stamped with the database's
  clock. A save from a form loaded before another admin's change is refused and the record is reloaded.
- Revenue report for admins by month or day and by car, saved as formula-safe CSV.
- Printable HTML rental contract for the renter and admins, HTML encoded under a strict Content-Security-Policy.
- Session version of the accounts (migration `AddSessionVersion`, column `OturumSurumu`).
- Tests: maintenance blocks and stale versions, calendar states, revenue sums and limits, CSV formula injection,
  contract encoding, policy and owner check, ended sessions, and SQL Server races of bookings with maintenance,
  of two admins saving one record, of a demoted admin's maintenance write, and a busy car lock under a short
  command timeout; the restricted login runs every new feature.
- Daily rolling file logs with Serilog under `%LOCALAPPDATA%\AracKiralama\logs`: files older than 30 days are
  deleted and at most 30 files are kept (tests check this with dated files). Error entries keep only exception
  types, HResult codes and method names.
- Anonymous audit entries in the log file: a fixed code and a role only, never an account, a car or a date. Every
  refused sign-in, also one with an empty field, writes the same code without a role, and a new account and a
  sign-up with taken values write the same neutral code, `auth.account.registration_submitted`.
- Central FluentValidation validators for sign-in, sign-up, password changes, the first admin, the admin's account
  form, the car form and the rental days and dates; the rules, the kept values of original accounts and cars and the
  Turkish messages are unchanged.
- More Moq and SQLite tests: validator limits and message order, the shortest and longest rental, the booking
  horizon, a quote that went stale overnight, cancel or return by day, the lock length, kept original account
  values, refused input that writes nothing, audit codes, and tests that the log files carry no personal data,
  setup keys or outcomes that tell accounts or rentals apart.
- English and Turkish README with an architecture diagram, the legacy upgrade and the first-run setup, a threat
  model (`docs/THREAT_MODEL.md`) and this changelog.
- Release job in CI: on `v*` tags the tag name is checked, a win-x64 build is published and uploaded as a zip
  artifact together with the READMEs, the changelogs and `docs/*.sql`.

### Changed

- A booking refused for taken days now says the car is not available on some of the days, the same for a rental or
  maintenance. A car with maintenance history cannot be deleted either.
- Errors are written to the log file instead of the trace output; the error dialogs are unchanged. Refused
  operations leave no log entry of their own.
- README claims of the original application (.NET Framework, ADO.NET) were replaced with the current stack.

### Removed

- The screenshots of the original application, whose screens no longer exist; the design update brings new ones.

### Security

- A role change or a password set by an admin ends every open session of the account for good, also when the change
  is undone later; every write checks the session version, the role and the account inside its own transaction.
- A lock request always gets a command timeout longer than the lock's wait, so a busy lock ends with the busy
  answer and nothing written, never with a command timeout.
- The application's login gets no `DELETE` on the maintenance log (`docs/grant-app-login.sql`).
- User names, e-mail addresses, phone numbers, passwords, setup keys, cars, plates, amounts and exception messages
  are never written to the log.

## Foundation

The rewrite of the original application, merged before this changelog was started.

### Added

- Layered .NET 10 solution (Domain, Business, Data, UI) with EF Core, dependency injection and automatic
  migrations; the original database is upgraded in place, keeping its data.
- Bookings as half-open day ranges with a start day of the customer's choice, configurable shortest and longest
  rental, booking horizon and long rental discount; cancellation before the pick-up day and return once under way,
  without deleting rows; filters for the catalog and the admin's rental list.
- First-run setup for an empty database; for a database with accounts but no admin, a single use setup key that
  only the database owner issues with `docs/issue-setup-key.sql`, and example rights for the application's login
  in `docs/grant-app-login.sql`.
- Optional demo mode (`ARAC_DEMO`, `Seed:Demo`), off by default, with fictional cars and no account.
- Original artwork in place of third-party images, and car picture downloads limited to public addresses.
- Tests for the services on SQLite and for booking races, setup keys, the restricted login and the legacy upgrade
  on SQL Server; CI with a SQL Server job.

### Security

- PBKDF2 password hashes with an upgrade of plain text passwords on the next sign-in; lockout after 5 failed
  attempts for 15 minutes with the same answer for every failure, decided by the database's clock.
- Role and owner checks in every service, with the acting account's role read from the database for every
  operation and checked again inside every privileged write; another account's rental gets the same answer as a
  missing one.
- Bookings under a per-car lock with the overlap check in the same transaction, so a car is never booked twice for
  one day.
- No account with a known password in a default installation.
