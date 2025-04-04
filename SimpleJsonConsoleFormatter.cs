

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace DotnetJsonLoggerDemo;

public static class SimpleJsonConsoleFormatterExtensions
{
    public static ILoggingBuilder AddSimpleJsonConsoleFormatter(this ILoggingBuilder builder, Action<JsonConsoleFormatterOptions>? configure = null)
    {
        configure ??= options => { };
        builder.AddConsoleFormatter<SimpleJsonConsoleFormatter, JsonConsoleFormatterOptions>(configure);
        return builder;
    }
}

internal sealed class SimpleJsonConsoleFormatter : ConsoleFormatter, IDisposable
{
    private readonly IDisposable? _optionsReloadToken;

    public SimpleJsonConsoleFormatter(IOptionsMonitor<JsonConsoleFormatterOptions> options)
        : base("simplejson")
    {
        ReloadLoggerOptions(options.CurrentValue);
        _optionsReloadToken = options.OnChange(ReloadLoggerOptions);
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        if (logEntry.State is BufferedLogRecord bufferedRecord)
        {
            string message = bufferedRecord.FormattedMessage ?? string.Empty;
            WriteInternal(null, textWriter, message, bufferedRecord.LogLevel, logEntry.Category, bufferedRecord.EventId.Id, bufferedRecord.Exception,
                bufferedRecord.Attributes.Count > 0, null, bufferedRecord.Attributes, bufferedRecord.Timestamp);
        }
        else
        {
            string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (logEntry.Exception == null && message == null)
            {
                return;
            }

            DateTimeOffset stamp = FormatterOptions.TimestampFormat != null
                ? (FormatterOptions.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now)
                : DateTimeOffset.MinValue;

            // We extract most of the work into a non-generic method to save code size. If this was left in the generic
            // method, we'd get generic specialization for all TState parameters, but that's unnecessary.
            WriteInternal(scopeProvider, textWriter, message, logEntry.LogLevel, logEntry.Category, logEntry.EventId.Id, logEntry.Exception?.ToString(),
                logEntry.State != null, logEntry.State?.ToString(), logEntry.State as IReadOnlyList<KeyValuePair<string, object?>>, stamp);
        }
    }

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
                    if (stateProperties != null)
                    {
                        foreach (KeyValuePair<string, object?> item in stateProperties)
                        {
                            WriteItem(writer, item);
                        }
                    }
                }
                WriteScopeInformation(writer, scopeProvider);
                writer.WriteEndObject();
                writer.Flush();
            }

            var messageBytes = output.WrittenSpan;
            var logMessageBuffer = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(messageBytes.Length));
            try
            {
                var charsWritten = Encoding.UTF8.GetChars(messageBytes, logMessageBuffer);
                textWriter.Write(logMessageBuffer, 0, charsWritten);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(logMessageBuffer);
            }
        }
        textWriter.Write(Environment.NewLine);
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }

    private void WriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider? scopeProvider)
    {
        if (FormatterOptions.IncludeScopes && scopeProvider != null)
        {
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> scopeItems)
                {
                    foreach (KeyValuePair<string, object?> item in scopeItems)
                    {
                        WriteItem(state, item);
                    }
                }
                else
                {
                    state.WriteStringValue(ToInvariantString(scope));
                }
            }, writer);
        }
    }

    private static void WriteItem(Utf8JsonWriter writer, KeyValuePair<string, object?> item)
    {
        var key = item.Key;
        switch (item.Value)
        {
            case bool boolValue:
                writer.WriteBoolean(key, boolValue);
                break;
            case byte byteValue:
                writer.WriteNumber(key, byteValue);
                break;
            case sbyte sbyteValue:
                writer.WriteNumber(key, sbyteValue);
                break;
            case char charValue:
                writer.WriteString(key, MemoryMarshal.CreateSpan(ref charValue, 1));
                break;
            case decimal decimalValue:
                writer.WriteNumber(key, decimalValue);
                break;
            case double doubleValue:
                writer.WriteNumber(key, doubleValue);
                break;
            case float floatValue:
                writer.WriteNumber(key, floatValue);
                break;
            case int intValue:
                writer.WriteNumber(key, intValue);
                break;
            case uint uintValue:
                writer.WriteNumber(key, uintValue);
                break;
            case long longValue:
                writer.WriteNumber(key, longValue);
                break;
            case ulong ulongValue:
                writer.WriteNumber(key, ulongValue);
                break;
            case short shortValue:
                writer.WriteNumber(key, shortValue);
                break;
            case ushort ushortValue:
                writer.WriteNumber(key, ushortValue);
                break;
            case null:
                writer.WriteNull(key);
                break;
            default:
                writer.WriteString(key, ToInvariantString(item.Value));
                break;
        }
    }

    private static string? ToInvariantString(object? obj) => Convert.ToString(obj, CultureInfo.InvariantCulture);

    internal JsonConsoleFormatterOptions FormatterOptions { get; set; }

    [MemberNotNull(nameof(FormatterOptions))]
    private void ReloadLoggerOptions(JsonConsoleFormatterOptions options)
    {
        FormatterOptions = options;
    }

    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
    }

    internal sealed class PooledByteBufferWriter : PipeWriter, IDisposable
    {
        private const int MinimumBufferSize = 256;

        private ArrayBuffer _buffer;
        private readonly Stream? _stream;

        public PooledByteBufferWriter(int initialCapacity)
        {
            _buffer = new ArrayBuffer(initialCapacity, usePool: true);
        }

        public PooledByteBufferWriter(int initialCapacity, Stream stream) : this(initialCapacity)
        {
            _stream = stream;
        }

        public ReadOnlySpan<byte> WrittenSpan => _buffer.ActiveSpan;

        public ReadOnlyMemory<byte> WrittenMemory => _buffer.ActiveMemory;

        public int Capacity => _buffer.Capacity;

        public void Clear() => _buffer.Discard(_buffer.ActiveLength);

        public void ClearAndReturnBuffers() => _buffer.ClearAndReturnBuffer();

        public void Dispose() => _buffer.Dispose();

        public void InitializeEmptyInstance(int initialCapacity)
        {
            Debug.Assert(initialCapacity > 0);
            Debug.Assert(_buffer.ActiveLength == 0);

            _buffer.EnsureAvailableSpace(initialCapacity);
        }

        public static PooledByteBufferWriter CreateEmptyInstanceForCaching() => new PooledByteBufferWriter(initialCapacity: 0);

        public override void Advance(int count) => _buffer.Commit(count);

        public override Memory<byte> GetMemory(int sizeHint = MinimumBufferSize)
        {
            Debug.Assert(sizeHint > 0);

            _buffer.EnsureAvailableSpace(sizeHint);
            return _buffer.AvailableMemory;
        }

        public override Span<byte> GetSpan(int sizeHint = MinimumBufferSize)
        {
            Debug.Assert(sizeHint > 0);

            _buffer.EnsureAvailableSpace(sizeHint);
            return _buffer.AvailableSpan;
        }

        internal void WriteToStream(Stream destination) => destination.Write(_buffer.ActiveSpan);

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            Debug.Assert(_stream is not null);
            await _stream.WriteAsync(WrittenMemory, cancellationToken).ConfigureAwait(false);
            Clear();

            return new FlushResult(isCanceled: false, isCompleted: false);
        }

        public override bool CanGetUnflushedBytes => true;
        public override long UnflushedBytes => _buffer.ActiveLength;

        // This type is used internally in JsonSerializer to help buffer and flush bytes to the underlying Stream.
        // It's only pretending to be a PipeWriter and doesn't need Complete or CancelPendingFlush for the internal usage.
        public override void CancelPendingFlush() => throw new NotImplementedException();
        public override void Complete(Exception? exception = null) => throw new NotImplementedException();
    }

    // Warning: Mutable struct!
    // The purpose of this struct is to simplify buffer management.
    // It manages a sliding buffer where bytes can be added at the end and removed at the beginning.
    // [ActiveSpan/Memory] contains the current buffer contents; these bytes will be preserved
    // (copied, if necessary) on any call to EnsureAvailableBytes.
    // [AvailableSpan/Memory] contains the available bytes past the end of the current content,
    // and can be written to in order to add data to the end of the buffer.
    // Commit(byteCount) will extend the ActiveSpan by [byteCount] bytes into the AvailableSpan.
    // Discard(byteCount) will discard [byteCount] bytes as the beginning of the ActiveSpan.

    [StructLayout(LayoutKind.Auto)]
    internal struct ArrayBuffer : IDisposable
    {
        private static int ArrayMaxLength => Array.MaxLength;

        private readonly bool _usePool;
        private byte[] _bytes;
        private int _activeStart;
        private int _availableStart;

        // Invariants:
        // 0 <= _activeStart <= _availableStart <= bytes.Length

        public ArrayBuffer(int initialSize, bool usePool = false)
        {
            Debug.Assert(initialSize > 0 || usePool);

            _usePool = usePool;
            _bytes = initialSize == 0
                ? Array.Empty<byte>()
                : usePool ? ArrayPool<byte>.Shared.Rent(initialSize) : new byte[initialSize];
            _activeStart = 0;
            _availableStart = 0;
        }

        public ArrayBuffer(byte[] buffer)
        {
            Debug.Assert(buffer.Length > 0);

            _usePool = false;
            _bytes = buffer;
            _activeStart = 0;
            _availableStart = 0;
        }

        public void Dispose()
        {
            _activeStart = 0;
            _availableStart = 0;

            byte[] array = _bytes;
            _bytes = null!;

            if (array is not null)
            {
                ReturnBufferIfPooled(array);
            }
        }

        // This is different from Dispose as the instance remains usable afterwards (_bytes will not be null).
        public void ClearAndReturnBuffer()
        {
            Debug.Assert(_usePool);
            Debug.Assert(_bytes is not null);

            _activeStart = 0;
            _availableStart = 0;

            byte[] bufferToReturn = _bytes;
            _bytes = Array.Empty<byte>();
            ReturnBufferIfPooled(bufferToReturn);
        }

        public int ActiveLength => _availableStart - _activeStart;
        public Span<byte> ActiveSpan => new Span<byte>(_bytes, _activeStart, _availableStart - _activeStart);
        public ReadOnlySpan<byte> ActiveReadOnlySpan => new ReadOnlySpan<byte>(_bytes, _activeStart, _availableStart - _activeStart);
        public Memory<byte> ActiveMemory => new Memory<byte>(_bytes, _activeStart, _availableStart - _activeStart);

        public int AvailableLength => _bytes.Length - _availableStart;
        public Span<byte> AvailableSpan => _bytes.AsSpan(_availableStart);
        public Memory<byte> AvailableMemory => _bytes.AsMemory(_availableStart);
        public Memory<byte> AvailableMemorySliced(int length) => new Memory<byte>(_bytes, _availableStart, length);

        public int Capacity => _bytes.Length;
        public int ActiveStartOffset => _activeStart;

        public byte[] DangerousGetUnderlyingBuffer() => _bytes;

        public void Discard(int byteCount)
        {
            Debug.Assert(byteCount <= ActiveLength, $"Expected {byteCount} <= {ActiveLength}");
            _activeStart += byteCount;

            if (_activeStart == _availableStart)
            {
                _activeStart = 0;
                _availableStart = 0;
            }
        }

        public void Commit(int byteCount)
        {
            Debug.Assert(byteCount <= AvailableLength);
            _availableStart += byteCount;
        }

        // Ensure at least [byteCount] bytes to write to.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureAvailableSpace(int byteCount)
        {
            if (byteCount > AvailableLength)
            {
                EnsureAvailableSpaceCore(byteCount);
            }
        }

        private void EnsureAvailableSpaceCore(int byteCount)
        {
            Debug.Assert(AvailableLength < byteCount);

            if (_bytes.Length == 0)
            {
                Debug.Assert(_usePool && _activeStart == 0 && _availableStart == 0);
                _bytes = ArrayPool<byte>.Shared.Rent(byteCount);
                return;
            }

            int totalFree = _activeStart + AvailableLength;
            if (byteCount <= totalFree)
            {
                // We can free up enough space by just shifting the bytes down, so do so.
                Buffer.BlockCopy(_bytes, _activeStart, _bytes, 0, ActiveLength);
                _availableStart = ActiveLength;
                _activeStart = 0;
                Debug.Assert(byteCount <= AvailableLength);
                return;
            }

            int desiredSize = ActiveLength + byteCount;

            if ((uint)desiredSize > ArrayMaxLength)
            {
                throw new OutOfMemoryException();
            }

            // Double the existing buffer size (capped at Array.MaxLength).
            int newSize = Math.Max(desiredSize, (int)Math.Min(ArrayMaxLength, 2 * (uint)_bytes.Length));

            byte[] newBytes = _usePool ?
                ArrayPool<byte>.Shared.Rent(newSize) :
                new byte[newSize];
            byte[] oldBytes = _bytes;

            if (ActiveLength != 0)
            {
                Buffer.BlockCopy(oldBytes, _activeStart, newBytes, 0, ActiveLength);
            }

            _availableStart = ActiveLength;
            _activeStart = 0;

            _bytes = newBytes;
            ReturnBufferIfPooled(oldBytes);

            Debug.Assert(byteCount <= AvailableLength);
        }

        public void Grow()
        {
            EnsureAvailableSpaceCore(AvailableLength + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReturnBufferIfPooled(byte[] buffer)
        {
            // The buffer may be Array.Empty<byte>()
            if (_usePool && buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}