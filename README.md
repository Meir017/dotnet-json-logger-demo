# custom JSON console formatter

Custom JSON console formatter that formats logs in a more readable way.
It formats the entire log message as a JSON object, without nested objects for `State` or `Scopes`.

*An assumption is made that there are no duplicate keys in the log message's state and scopes.*

Sample output:

```json
{"EventId":14,"LogLevel":"Information","Category":"Microsoft.Hosting.Lifetime","Message":"Now listening on: http://localhost:5113","address":"http://localhost:5113","{OriginalFormat}":"Now listening on: {address}"}
{"EventId":0,"LogLevel":"Information","Category":"Microsoft.Hosting.Lifetime","Message":"Application started. Press Ctrl\u002BC to shut down.","{OriginalFormat}":"Application started. Press Ctrl\u002BC to shut down."}
{"EventId":0,"LogLevel":"Information","Category":"Microsoft.Hosting.Lifetime","Message":"Hosting environment: Development","EnvName":"Development","{OriginalFormat}":"Hosting environment: {EnvName}"}
{"EventId":0,"LogLevel":"Information","Category":"Microsoft.Hosting.Lifetime","Message":"Content root path: D:\\Repos\\Meir017\\dotnet-json-logger-demo","ContentRoot":"D:\\Repos\\Meir017\\dotnet-json-logger-demo","{OriginalFormat}":"Content root path: {ContentRoot}"}
{"EventId":3,"LogLevel":"Warning","Category":"Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware","Message":"Failed to determine the https port for redirect.","{OriginalFormat}":"Failed to determine the https port for redirect.","SpanId":"fbbb1fc13b5df511","TraceId":"4b6c0308af9a6b5364192e3bcd92b717","ParentId":"0000000000000000","ConnectionId":"0HNBJ84V962V6","RequestId":"0HNBJ84V962V6:00000001","RequestPath":"/weatherforecast/"}
{"EventId":1,"LogLevel":"Information","Category":"Microsoft.AspNetCore.Hosting.Diagnostics","Message":"Request starting HTTP/1.1 GET http://localhost:5113/weatherforecast/ - - -","Protocol":"HTTP/1.1","Method":"GET","ContentType":null,"ContentLength":null,"Scheme":"http","Host":"localhost:5113","PathBase":"","Path":"/weatherforecast/","QueryString":"","{OriginalFormat}":"Request starting {Protocol} {Method} {Scheme}://{Host}{PathBase}{Path}{QueryString} - {ContentType} {ContentLength}","SpanId":"7cd33e38e59bf705","TraceId":"ec870c5cac10f855de8dd675b4939e53","ParentId":"0000000000000000","ConnectionId":"0HNBJ84V962V8","RequestId":"0HNBJ84V962V8:00000001","RequestPath":"/weatherforecast/"}
{"EventId":0,"LogLevel":"Information","Category":"Microsoft.AspNetCore.Routing.EndpointMiddleware","Message":"Executing endpoint \u0027HTTP: GET /weatherforecast\u0027","EndpointName":"HTTP: GET /weatherforecast","{OriginalFormat}":"Executing endpoint \u0027{EndpointName}\u0027","SpanId":"7cd33e38e59bf705","TraceId":"ec870c5cac10f855de8dd675b4939e53","ParentId":"0000000000000000","ConnectionId":"0HNBJ84V962V8","RequestId":"0HNBJ84V962V8:00000001","RequestPath":"/weatherforecast/"}
{"EventId":1,"LogLevel":"Information","Category":"Microsoft.AspNetCore.Routing.EndpointMiddleware","Message":"Executed endpoint \u0027HTTP: GET /weatherforecast\u0027","EndpointName":"HTTP: GET /weatherforecast","{OriginalFormat}":"Executed endpoint \u0027{EndpointName}\u0027","SpanId":"7cd33e38e59bf705","TraceId":"ec870c5cac10f855de8dd675b4939e53","ParentId":"0000000000000000","ConnectionId":"0HNBJ84V962V8","RequestId":"0HNBJ84V962V8:00000001","RequestPath":"/weatherforecast/"}
{"EventId":2,"LogLevel":"Information","Category":"Microsoft.AspNetCore.Hosting.Diagnostics","Message":"Request finished HTTP/1.1 GET http://localhost:5113/weatherforecast/ - 200 - application/json;\u002Bcharset=utf-8 7.7483ms","ElapsedMilliseconds":7.7483,"StatusCode":200,"ContentType":"application/json; charset=utf-8","ContentLength":null,"Protocol":"HTTP/1.1","Method":"GET","Scheme":"http","Host":"localhost:5113","PathBase":"","Path":"/weatherforecast/","QueryString":"","{OriginalFormat}":"Request finished {Protocol} {Method} {Scheme}://{Host}{PathBase}{Path}{QueryString} - {StatusCode} {ContentLength} {ContentType} {ElapsedMilliseconds}ms","SpanId":"7cd33e38e59bf705","TraceId":"ec870c5cac10f855de8dd675b4939e53","ParentId":"0000000000000000","ConnectionId":"0HNBJ84V962V8","RequestId":"0HNBJ84V962V8:00000001","RequestPath":"/weatherforecast/"}
{"EventId":0,"LogLevel":"Information","Category":"Microsoft.Hosting.Lifetime","Message":"Application is shutting down...","{OriginalFormat}":"Application is shutting down..."}
```

## Remarks

This JSON console formatter is heavily based on https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Logging.Console/src/JsonConsoleFormatter.cs.

However, due to the class being internal and some of it's dependencies being internal as well, I had to copy a lot of code from the original class to make it work.

this includes copying the internal [`PooledByteBufferWriter`](https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/Json/PooledByteBufferWriter.cs) class and its dependency [`ArrayBuffer`](https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Net/ArrayBuffer.cs) classes.

**Ideally** - the [`JsonConsoleFormatter`](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Logging.Console/src/JsonConsoleFormatter.cs) class would be public and expose virtual methods to allow customizing of the json format.

example:

existing code:
```cs
private void WriteInternal(IExternalScopeProvider? scopeProvider, TextWriter textWriter, string? message, LogLevel logLevel,
            string category, int eventId, string? exception, bool hasState, string? stateMessage, IReadOnlyList<KeyValuePair<string, object?>>? stateProperties,
            DateTimeOffset stamp)
        {
            const int DefaultBufferSize = 1024;
            using (var output = new PooledByteBufferWriter(DefaultBufferSize))
            {
                using (var writer = new Utf8JsonWriter(output, FormatterOptions.JsonWriterOptions))
                {
                    writer.WriteStartObject();
                    var timestampFormat = FormatterOptions.TimestampFormat;
                    if (timestampFormat != null)
                    {
                        writer.WriteString("Timestamp", stamp.ToString(timestampFormat));
                    }
                    writer.WriteNumber(nameof(LogEntry<object>.EventId), eventId);
                    writer.WriteString(nameof(LogEntry<object>.LogLevel), GetLogLevelString(logLevel));
                    writer.WriteString(nameof(LogEntry<object>.Category), category);
                    writer.WriteString("Message", message);

                    if (exception != null)
                    {
                        writer.WriteString(nameof(Exception), exception);
                    }

                    if (hasState)
                    {
                        writer.WriteStartObject(nameof(LogEntry<object>.State));
                        writer.WriteString("Message", stateMessage);
                        if (stateProperties != null)
                        {
                            foreach (KeyValuePair<string, object?> item in stateProperties)
                            {
                                WriteItem(writer, item);
                            }
                        }
                        writer.WriteEndObject();
                    }
                    WriteScopeInformation(writer, scopeProvider);
                    writer.WriteEndObject();
                    writer.Flush();
                }

                var messageBytes = output.WrittenSpan;
                var logMessageBuffer = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(messageBytes.Length));
                try
                {
 #if NET
                    var charsWritten = Encoding.UTF8.GetChars(messageBytes, logMessageBuffer);
 #else
                    int charsWritten;
                    unsafe
                    {
                        fixed (byte* messageBytesPtr = messageBytes)
                        fixed (char* logMessageBufferPtr = logMessageBuffer)
                        {
                            charsWritten = Encoding.UTF8.GetChars(messageBytesPtr, messageBytes.Length, logMessageBufferPtr, logMessageBuffer.Length);
                        }
                    }
 #endif
                    textWriter.Write(logMessageBuffer, 0, charsWritten);
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(logMessageBuffer);
                }
            }
            textWriter.Write(Environment.NewLine);
        }

private static string GetLogLevelString(...) { ... }

private static void WriteItem(...) { ... }

private void WriteScopeInformation(...) { ... }

private static string? ToInvariantString(...) { ... }
```

suggested code:

```cs
private void WriteInternal(IExternalScopeProvider? scopeProvider, TextWriter textWriter, string? message, LogLevel logLevel,
            string category, int eventId, string? exception, bool hasState, string? stateMessage, IReadOnlyList<KeyValuePair<string, object?>>? stateProperties,
            DateTimeOffset stamp)
        {
            const int DefaultBufferSize = 1024;
            using (var output = new PooledByteBufferWriter(DefaultBufferSize))
            {
                using (var writer = new Utf8JsonWriter(output, FormatterOptions.JsonWriterOptions))
                {
                    WriteJsonObject(writer, stamp, logLevel, category, message, eventId, exception, hasState, stateMessage, stateProperties);
                    writer.Flush();
                }

                var messageBytes = output.WrittenSpan;
                var logMessageBuffer = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(messageBytes.Length));
                try
                {
 #if NET
                    var charsWritten = Encoding.UTF8.GetChars(messageBytes, logMessageBuffer);
 #else
                    int charsWritten;
                    unsafe
                    {
                        fixed (byte* messageBytesPtr = messageBytes)
                        fixed (char* logMessageBufferPtr = logMessageBuffer)
                        {
                            charsWritten = Encoding.UTF8.GetChars(messageBytesPtr, messageBytes.Length, logMessageBufferPtr, logMessageBuffer.Length);
                        }
                    }
 #endif
                    textWriter.Write(logMessageBuffer, 0, charsWritten);
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(logMessageBuffer);
                }
            }
            textWriter.Write(Environment.NewLine);
        }

protected virtual void WriteJsonObject(Utf8JsonWriter writer, DateTimeOffset stamp, LogLevel logLevel, string category, string? message,
            int eventId, string? exception, bool hasState, string? stateMessage, IReadOnlyList<KeyValuePair<string, object?>>? stateProperties)
{
    writer.WriteStartObject();
    var timestampFormat = FormatterOptions.TimestampFormat;
    if (timestampFormat != null)
    {
        writer.WriteString("Timestamp", stamp.ToString(timestampFormat));
    }
    writer.WriteNumber(nameof(LogEntry<object>.EventId), eventId);
    writer.WriteString(nameof(LogEntry<object>.LogLevel), GetLogLevelString(logLevel));
    writer.WriteString(nameof(LogEntry<object>.Category), category);
    writer.WriteString("Message", message);

    if (exception != null)
    {
        writer.WriteString(nameof(Exception), exception);
    }

    if (hasState)
    {
        writer.WriteStartObject(nameof(LogEntry<object>.State));
        writer.WriteString("Message", stateMessage);
        if (stateProperties != null)
        {
            foreach (KeyValuePair<string, object?> item in stateProperties)
            {
                WriteItem(writer, item);
            }
        }
        writer.WriteEndObject();
    }
    WriteScopeInformation(writer, scopeProvider);
    writer.WriteEndObject();
}

protected static string GetLogLevelString(...) { ... }

protected static void WriteItem(...) { ... }

protected virtual void WriteScopeInformation(...) { ... }

protected static string? ToInvariantString(...) { ... }
```
