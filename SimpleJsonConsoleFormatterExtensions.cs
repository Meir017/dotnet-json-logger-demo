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