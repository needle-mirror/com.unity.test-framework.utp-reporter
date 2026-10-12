# Unity Test Protocol Reporter

`com.unity.test-framework.utp-reporter` emits UTP test status messages from player test runs.

## Output mode

Use `-utp-output-mode` to select transport:

- `player-connection` (default)
- `log`

When `log` is selected, messages are emitted with the `##utp:` prefix so UTR log-reading can deserialize them.
