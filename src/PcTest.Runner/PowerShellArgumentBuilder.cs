using System.Globalization;

namespace PcTest.Runner;

public static class PowerShellArgumentBuilder
{
    public static List<string> BuildArgumentList(Dictionary<string, object?> inputs)
    {
        var args = new List<string>();
        foreach (var input in inputs)
        {
            if (input.Value is null)
            {
                continue;
            }

            args.Add($"-{input.Key}");
            AppendArgumentValues(args, input.Value);
        }

        return args;
    }

    private static void AppendArgumentValues(ICollection<string> arguments, object value)
    {
        if (value is string s)
        {
            arguments.Add(s);
            return;
        }

        if (value is bool b)
        {
            arguments.Add(b ? "$true" : "$false");
            return;
        }

        if (value is int i)
        {
            arguments.Add(i.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (value is double d)
        {
            arguments.Add(d.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (value is IEnumerable<object> list)
        {
            foreach (var item in list)
            {
                AppendArgumentValues(arguments, item);
            }

            return;
        }

        if (value is IEnumerable<string> strings)
        {
            foreach (var item in strings)
            {
                arguments.Add(item);
            }

            return;
        }

        if (value is Array array)
        {
            foreach (var item in array)
            {
                if (item is null)
                {
                    continue;
                }

                AppendArgumentValues(arguments, item);
            }

            return;
        }

        arguments.Add(value.ToString() ?? string.Empty);
    }
}
