# Changelog

🇬🇧 **English** · 🇹🇷 [Türkçe](CHANGELOG.tr.md)

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Real car photographs: twelve common models (Renault Clio, Fiat Egea, Toyota Corolla, Volkswagen Golf, Dacia Duster,
  Peugeot 2008, Tesla Model 3, Fiat 500, Ford Transit Custom, Volkswagen Touran, Mazda MX-5, Fiat Doblò) show a
  photograph from Wikimedia Commons on the car cards, the booking screen, the rental cards and the admin panel,
  matched by make and model. The photographs (CC0 and CC BY-SA, 640 pixels wide, about 650 KB together) are embedded
  in the application and never downloaded; a car's own picture address still replaces them, and any other car keeps
  the drawing. `docs/CREDITS.md` lists the author, license, source and changes of each one, and the READMEs link it
  with a note that the project is not affiliated with any vehicle manufacturer. The release archive carries
  `docs/CREDITS.md` and the license.
- UI tests: every bundled photograph is credited in `docs/CREDITS.md`, stays within the size limits and is in the
  catalog; makes and models are matched with case, accents, punctuation and trims; the photograph is drawn in the
  light, dark and high contrast palettes and unknown models keep the drawing.
- New design: an own visual identity of asphalt graphite and signal orange, a road and location pin logo
  (`assets/logo.svg`, rasterized into `app.ico` and `logo-256.png`), headings, prices, plates and calendar days in
  Bahnschrift, and every screen rebuilt on one frame with a header, an inline message strip, the content and a footer.
- Light, dark and high contrast themes: `Appearance:Theme` (`Light`, `Dark` or `System`); high contrast uses the
  Windows system colours with native controls, and open screens follow a change of high contrast or the app mode.
- Car cards with a vehicle drawn in code in the car's colour, a plate chip and the daily price; a booking screen with
  a live return day and total; rental cards with a stage pill; themed lists for cars, accounts, maintenance records and
  the revenue report, whose rows carry the amount and a bar of its share; a calendar whose days show their state in
  words, a pattern and a tint, with a ring around today.
- Results and refusals appear inside the window instead of message boxes; questions use a themed message window.
  Refused fields get a danger border and their message as the accessible description.
- `tools/Screenshots`: the icon and the README screenshots at 1x and 2x, drawn offscreen with a sample fleet of
  common models and their bundled photographs; the README shows fourteen screens.
- UI tests: every screen inside working areas of 1024x600, 1366x768 and 1920x1040 at text sizes 1 to 2 with long
  names, themed controls, tab order, inline messages, palette contrast and identity, high contrast and its switch while
  screens are open, card and calendar layout and the vehicle art.

- Availability calendar of a car: every day of a month free, booked, in maintenance or past by the database's
  today, with the state written in each cell (customers for listed cars, admins for any car).
- Maintenance and damage log for admins (migration `AddMaintenanceLog`, table `AracBakim`): records block the car's
  days, cannot take days of an active rental, are cancelled instead of deleted and are stamped with the database's
  clock. A save from a form loaded before another admin's change is refused and the record is reloaded.
- Revenue report for admins by month or day and by car, saved as formula-safe CSV.
- Printable HTML rental contract for the renter and admins, HTML encoded under a strict Content-Security-Policy.
- Session version of the accounts (migration `AddSessionVersion`, column `OturumSurumu`).
- Version of the cars (migration `AddCarVersion`, column `Surum`): a car change from a form loaded before another
  admin's change is refused and the car is loaded again.
- Tests: stale account and car forms (also decided inside the write and raced on SQL Server), the own password
  change ending other sessions, the lock order of ending a rental and a deadlock victim on SQL Server, maintenance
  times stamped after a waited lock, overlapping maintenance records, kept maintenance input, the database's month
  and report range, quick calendar clicks, past days hidden from customers, the contract without the sign-in name,
  the revenue counted by today and the contract button at large fonts.
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

- The optional demo data adds the same kind of cars as the screenshots (Renault Clio, Fiat Egea, Ford Transit Custom,
  Fiat 500 and Tesla Model 3) instead of made-up brands; it still adds no account. Car pictures are a little wider
  (about 1.9 : 1) to match the photographs.
- The application is per-monitor DPI aware; windows fit their content, never open larger than the screen's working
  area and scroll their body on a small screen.
- Sign-up, the first admin, a password change and a booking close their window and the screen that opened them
  reports the result; an admin who changes their own role or password returns to the sign-in, which says why.
- Amounts group their thousands ("1.250,00 TL"). Picking a car, an account or a maintenance record works with the
  keyboard too; loading a list again is never a choice and keeps the record in the form.

- A booking refused for taken days now says the car is not available on some of the days, the same for a rental or
  maintenance. A car with maintenance history cannot be deleted either.
- Errors are written to the log file instead of the trace output; the error dialogs are unchanged. Refused
  operations leave no log entry of their own.
- README claims of the original application (.NET Framework, ADO.NET) were replaced with the current stack.
- The revenue report counts only rentals picked up by today (a later one can still be cancelled without charge),
  names the days of a month the range covers only in part, and says so in a note on the screen and in the CSV.
- The calendar opens on the month of the database's today, and the report starts with this month up to that day.
  Customers see every day before today as past. Quick clicks on the month buttons add up.
- A refused maintenance save keeps what the admin typed; only a record changed or cancelled elsewhere is loaded
  again. A maintenance record cannot share days with another active record of the car, and its times are taken
  when it is written, after the car's lock.
- The rental contract no longer prints the sign-in name; the renter is named by e-mail address and phone.
- An admin who changes their own role or password is signed out with an explanation instead of being refused at
  every later click.
- The contract button of a rental card sizes itself from its font, and the card keeps the system colours under high
  contrast.

### Removed

- The screenshots of the original application, whose screens no longer exist; the design update brought new ones.
- The old sign-in picture, backdrop, car placeholder and their Edge render script, and the data grids of the admin
  screens.

### Security

- A role change or a password set by an admin ends every open session of the account for good, also when the change
  is undone later; every write checks the session version, the role and the account inside its own transaction.
- An account change carries the edit version its form loaded (`DuzenlemeSurumu`, added by the
  `AddAccountEditVersion` migration and raised by every edit of an account), so a stale form can give back neither an
  admin role another admin just removed nor an e-mail address or phone number another admin just corrected; the
  session version keeps ending sessions on a new role or password only.
- Date decisions are made with the database's date read inside the write, after the car's lock was granted: whether
  ending a rental cancels or returns it, a booking's first day and horizon, and a maintenance record's day limits. A
  request that waited for the lock across midnight decides with the new day (tested on SQL Server with the database
  clock moved past midnight while the request waits).
- A failed sign-in is counted by one conditional `UPDATE` only while the account is unlocked by the database's clock
  at the write and still has the checked password and session version; the lock's end is computed there. Failures
  arriving together lock the account once, and failures held up behind an admin's password reset are not counted
  (tested on SQL Server with six failures held at a barrier).
- The booking screen's pick-up day limits and quotes and the newest model year of a car follow the database's clock
  instead of the workstation's.
- The account's own password change checks the session first, raises the session version and so ends every other
  session of the account, for example one opened with a stolen password; the window that changed it keeps working.
- Ending a rental takes the car's lock before it checks the acting account, in the same order as every other car
  write; a deadlock victim is answered with the busy message.
- The session version is a required value wherever a session, an actor or sign-in data is made.
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
