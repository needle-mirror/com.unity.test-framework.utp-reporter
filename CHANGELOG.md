## [1.3.3-exp.1] - 2026-10-12

### Changed
- Depend on `com.unity.external.test-protocol` `2.0.1-exp.1`, which reports a process id of 0 instead of throwing on
  platforms that do not support System.Diagnostics.Process, such as the web.

## [1.3.2-exp.1] - 2026-10-09

### Changed
- Released as experimental: the `com.unity.external.test-protocol` dependency has no released version, so the package
  cannot be released either. Content is `1.3.1` plus documentation of the public API and of the package.
- Dropped the Moq-based tests, which projects that inject this package cannot resolve.

## [1.3.1] - 2026-09-18

### Fixed
- Report the categories a test inherits from its fixture when the test method is declared in a base
  class. The fixture reports the class that ran, while the test reports the class that declares it, so
  the two disagree for every inherited test and the fixture's categories were dropped — which is most
  of what `1.3.0` set out to report (UTR-1349).

## [1.3.0] - 2026-08-31

### Added
- Report `categories` on both test messages, merging the categories a test declares itself with those
  declared on its fixture. Categories were never reported from player runs, so consumers that use
  them — performance data and the test datastore — only ever saw them from editor runs.
- Report `iteration` on both test messages, so a consumer no longer has to infer the attempt index
  from the shape of the results.

### Fixed
- Report `classname` and `methodname` on the test Begin message, not only on the End. A consumer
  identifies a test from the phase it can see, so an attempt that never reports an End — the player
  died while it was running — could not be matched to the test it belonged to and was counted as a
  separate test (UTR-1334).

## [1.2.0] - 2026-03-09

### Changed
- Add `-utp-output-mode` support with `player-connection` (default) and `log`.
- Emit `##utp:`-prefixed messages in `log` mode for UTR log-reading compatibility.
- Quit the player by default after reporting run completion.

## [1.1.0] - 2021-01-11
Add support for build configurations

## [1.0.2] - 2020-04-23
Fix meta files

## [1.0.1] - 2020-04-22
Upgrade test protocol version to avoid test assembly collisions

## [1.0.0] - 2020-04-01
Move out of preview

## [0.2.3] - 2020-03-31
Update UTP version to 1.0.0
Fix player build error when not including tests

## [0.1.3] - 2019-06-15
Fixed various compilation issues


## [0.1.2] - 2019-04-10

### This is the first release of Unity Test Test Protocol Reporter.

Initial implementation.