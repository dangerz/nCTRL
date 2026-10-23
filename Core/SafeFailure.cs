using System;

namespace NGarage.Core;

public static class SafeFailure
{
    // Fixed labels only: exception messages, inner exceptions and type names may contain secrets.
    public static string Describe(Exception error)
    {
        string code=error switch {
            PlatformNotSupportedException=>"platform-unsupported",
            NotSupportedException=>"runtime-unsupported",
            InvalidOperationException=>"runtime-state",
            TypeInitializationException=>"runtime-initialization",
            DllNotFoundException=>"native-library-missing",
            EntryPointNotFoundException=>"native-entry-missing",
            System.Text.Json.JsonException=>"response-format",
            ArgumentException=>"request-format",
            _=>"unexpected"
        };
        return $"Could not complete the request ({code}). No automatic retry was sent.";
    }
}
