# Targeted album, login, and theme corrections

User requirements: clear checkbox/add selection for library songs assigned to an album; successful Google login reaches Home; balanced login screen; Home setting icon becomes light/dark toggle and Settings belongs under Profile.

## Constraints

Keep the current Flutter/ASP.NET/SQL Server stack, Vietnamese/English copy, existing icon and Spotify palette. Preserve `.env` and all user data; do not migrate any database. Do not bypass backend Google verification or fabricate a successful login. Use actual native checkboxes/buttons and accessible, keyboard-safe layouts.

## Work

- [x] Album picker: select multiple songs, recognize existing memberships, confirm count, handle sequential concurrency versions and partial failures; regression tests.
- [x] Google auth: reproduce pushed-route success staying on Profile; explicitly select Home only after durable session acceptance, preserve cancellation/failure/stale guards; rebalance login layout with small-screen/keyboard regressions.
- [x] Home/Profile: light/dark toggle persists preferences, waits for account settings to avoid language loss, Settings remains in guest/account Profile; Home reloads after identity changes and clears prior-owner content.
- [x] Integrate code review, Flutter analyze/full tests (99/99), debug Android build with existing public OAuth config; install update on connected Samsung, verify cold launch/API, theme/Profile Settings and two-checkbox selection without committing library changes. Retain the existing signed-in session; new Google handshake and on-device Login layout remain unverified.
- [x] Record commands/results, installed APK digest, UI verification and live OAuth boundary in BUILD_LOG section 7.

Independent review covered album concurrency/selection/accessibility, Home preference hydration and final auth/session storage fencing. An additional owner-bound API client regression reproduced and fixed cross-account credentials on a retained GET retry; all four focused cases pass and a second reviewer found no blocker. Actual new Google provider authentication remains a device verification boundary; fake API/identity fixtures are test-only.
