# Using Local Chrome for Audio Tests

The audio publish tests now support using either a containerized Chrome (default) or your local Chrome browser for debugging.

## Environment Variable

Set the `USE_LOCAL_CHROME` environment variable to `true` to use your local Chrome browser:

```bash
export USE_LOCAL_CHROME=true
dotnet test LivekitRtc.Tests/LivekitRtc.Tests.csproj --filter "FullyQualifiedName~AudioPublishTests"
```

Or on Windows:
```powershell
$env:USE_LOCAL_CHROME="true"
dotnet test LivekitRtc.Tests/LivekitRtc.Tests.csproj --filter "FullyQualifiedName~AudioPublishTests"
```

## Requirements for Local Chrome

When using `USE_LOCAL_CHROME=true`:

1. **Chrome/Chromium installed**: Your local Chrome or Chromium browser must be installed
2. **ChromeDriver**: The Selenium ChromeDriver must be available and compatible with your Chrome version
   - ChromeDriver should be in your PATH, or
   - Selenium Manager will auto-download the compatible ChromeDriver

3. **Network Access**: Your local Chrome needs to access:
   - LiveKit server on the exposed port (e.g., `ws://localhost:XXXXX`)
   - HTTP server serving test files (e.g., `http://localhost:XXXXX`)

## How It Works

### With Local Chrome (`USE_LOCAL_CHROME=true`)
- Tests use your installed Chrome browser via ChromeDriver
- Chrome connects to LiveKit using the **external exposed URL** (e.g., `ws://localhost:57123`)
- Web test files are served via HTTP on an exposed port (e.g., `http://localhost:57124/receiver.html`)
- Allows for easier debugging with Chrome DevTools
- You can watch the browser in action

### Without Local Chrome (Default)
- Tests use a containerized Selenium Standalone Chrome
- All services communicate via Docker's internal network
- Chrome connects to LiveKit using the **internal container URL** (`ws://livekit:7880`)
- Web files served via internal URL (`http://webserver/receiver.html`)
- Fully isolated, no dependencies on local browser installation

## Debugging

When using local Chrome, you can:
1. See the browser window open during test execution
2. Inspect the page with Chrome DevTools
3. View console logs and network traffic
4. Debug JavaScript execution in the test page

## Example

```bash
# Run with local Chrome and see the browser
export USE_LOCAL_CHROME=true
dotnet test LivekitRtc.Tests/LivekitRtc.Tests.csproj \
  --filter "FullyQualifiedName~AudioPublishTests.PublishAudioTrack_ChromeReceivesAudio"

# Run with containerized Chrome (default, headless)
unset USE_LOCAL_CHROME
dotnet test LivekitRtc.Tests/LivekitRtc.Tests.csproj \
  --filter "FullyQualifiedName~AudioPublishTests.PublishAudioTrack_ChromeReceivesAudio"
```

## Technical Details

The implementation automatically:
- Skips creating the Chrome Docker container when using local Chrome
- Adjusts the LiveKit WebSocket URL to use the externally exposed port
- Adjusts the web files HTTP URL to use the externally exposed port
- Uses `ChromeDriver` instead of `RemoteWebDriver` for local execution

Both LiveKit server, Redis, and the HTTP file server still run in Docker containers regardless of the Chrome mode.
