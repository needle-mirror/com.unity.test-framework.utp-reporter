## [2.0.0-exp.3] - 2026-11-17

### Fixed
- Report the suite's duration on the `TestGroup` end message, in milliseconds in `duration` as the editor reporter
  does. Without it a consumer that closes a group span on its duration kept every suite open.

## [2.0.0-exp.2] - 2026-10-12

### Changed
- Depend on `com.unity.external.test-protocol` `2.0.1-exp.1`, which reports a process id of 0 instead of throwing on
  platforms that do not support System.Diagnostics.Process, such as the web.

## [2.0.0-exp.1] - 2026-09-24

### Added
- Report `uniqueName` on both test messages. It is the key a consumer matches a test on, it is not
  an NUnit XML attribute, and it was therefore unreachable while the reporter read a serialized copy
  of the node instead of the node itself (UTR-1334).
- Announce `spanId` and `parentSpanId` on every message. Ids are derived the same way the editor
  reporter derives them - SHA256 over `salt:key[:iteration]`, keyed on `uniqueName` - with a fresh
  per-process salt rather than the process id, which the OS reuses.

### Changed
- Source test identity and hierarchy from the `ITest` / `ITestResult` the callback hands over,
  instead of serializing them to XML and parsing the result back. A node's parent is
  `ITest.Parent`, so grouping no longer depends on the order events happen to arrive in, and
  fixture-declared categories are found by walking the tree rather than being remembered by class
  name.
- Report a suite named by its assembly path under the file name rather than the full on-disk path.
- Report `iteration` only when an attempt index is non-zero, rather than always writing a `0`.

### Removed
- `INUnitXmlToUTPConverter` / `NUnitXmlToUTPConverter`, replaced by `INUnitToUtpConverter` /
  `NUnitToUtpConverter`, which take the NUnit nodes directly. These types were public, so this is a
  breaking change for any out-of-tree caller; the reporter itself was their only known caller, and
  the `start-test`, `start-suite` and `test-output` branches they carried were unreachable from it
  because `ITest.ToXml` cannot produce those element names.

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