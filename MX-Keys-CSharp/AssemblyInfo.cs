using System.Runtime.CompilerServices;

// Lets the test project construct MeetBridge with a test-only port/secret instead of the real
// singleton's hardcoded port and on-disk pairing secret — see MeetBridge's internal constructor.
[assembly: InternalsVisibleTo("MxKeysGoogleMeetPlugin.Tests")]
