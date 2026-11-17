using System.Globalization;

namespace WinOrbit.Launcher;

/// <summary>
/// A small, safe expression evaluator for the launcher: + − × ÷ ^ %, parentheses, unary minus, constants
/// (pi, e) and functions (sqrt, abs, round, floor, ceil, sin, cos, tan, log, ln). No code execution, no eval.
/// </summary>
public static class Calculator
{
    public static bool TryEvaluate(string input, out double result)
    {
        result = 0;
        var s = input.Trim().TrimStart('=').Replace('×', '*').Replace('÷', '/').Replace(',', '.').Replace(" ", "");
        if (s.Length == 0) return false;
        // Needs at least one operator or function so a plain number or word isn't treated as a sum.
        if (!s.Any(c => "+-*/^%()".Contains(c)) && !s.Contains("sqrt") && !s.Contains("pi")) return false;
        try
        {
            var p = new Parser(s);
            result = p.Expression();
            return p.AtEnd && !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch { return false; }
    }

    public static string Format(double v)
    {
        if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-6 && v != 0)) return v.ToString("0.######E+0", CultureInfo.CurrentCulture);
        return Math.Round(v, 10).ToString("#,0.##########", CultureInfo.CurrentCulture);
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;
        public Parser(string s) => _s = s.ToLowerInvariant();
        public bool AtEnd => _i >= _s.Length;
        private char Peek => _i < _s.Length ? _s[_i] : '\0';

        public double Expression()
        {
            var v = Term();
            while (Peek is '+' or '-') { var op = _s[_i++]; var r = Term(); v = op == '+' ? v + r : v - r; }
            return v;
        }

        private double Term()
        {
            var v = Power();
            while (Peek is '*' or '/' or '%')
            {
                var op = _s[_i++];
                if (op == '%' && (AtEnd || Peek is '+' or '-' or ')' or '*' or '/')) { v /= 100; continue; }   // "50%" means 0.5
                var r = Power();
                v = op == '*' ? v * r : op == '/' ? v / r : v % r;
            }
            return v;
        }

        private double Power()
        {
            var b = Unary();
            if (Peek == '^') { _i++; return Math.Pow(b, Power()); }   // right-associative
            return b;
        }

        private double Unary()
        {
            if (Peek == '-') { _i++; return -Unary(); }
            if (Peek == '+') { _i++; return Unary(); }
            return Primary();
        }

        private double Primary()
        {
            if (Peek == '(')
            {
                _i++;
                var v = Expression();
                if (Peek != ')') throw new FormatException();
                _i++;
                return v;
            }
            if (char.IsLetter(Peek))
            {
                var start = _i;
                while (char.IsLetter(Peek)) _i++;
                var name = _s[start.._i];
                if (name == "pi") return Math.PI;
                if (name == "e") return Math.E;
                if (Peek != '(') throw new FormatException();
                var arg = Primary();
                return name switch
                {
                    "sqrt" => Math.Sqrt(arg), "abs" => Math.Abs(arg), "round" => Math.Round(arg), "floor" => Math.Floor(arg), "ceil" => Math.Ceiling(arg),
                    "sin" => Math.Sin(arg), "cos" => Math.Cos(arg), "tan" => Math.Tan(arg), "log" => Math.Log10(arg), "ln" => Math.Log(arg),
                    _ => throw new FormatException()
                };
            }
            var s0 = _i;
            while (char.IsDigit(Peek) || Peek == '.') _i++;
            if (s0 == _i) throw new FormatException();
            return double.Parse(_s[s0.._i], CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Everyday unit conversion: "10 km to mi", "70 f in c", "2.5 kg to lb", "500 mb to gb".</summary>
public static class Units
{
    private static readonly (string[] Names, string Group, double Factor)[] Table =
    {
        (new[] { "mm" }, "len", 0.001), (new[] { "cm" }, "len", 0.01), (new[] { "m", "meter", "meters", "metre", "metres" }, "len", 1), (new[] { "km" }, "len", 1000),
        (new[] { "in", "inch", "inches" }, "len", 0.0254), (new[] { "ft", "foot", "feet" }, "len", 0.3048), (new[] { "yd", "yard", "yards" }, "len", 0.9144), (new[] { "mi", "mile", "miles" }, "len", 1609.344),
        (new[] { "mg" }, "mass", 0.001), (new[] { "g", "gram", "grams" }, "mass", 1), (new[] { "kg" }, "mass", 1000), (new[] { "oz" }, "mass", 28.349523125), (new[] { "lb", "lbs", "pound", "pounds" }, "mass", 453.59237), (new[] { "st", "stone" }, "mass", 6350.29318),
        (new[] { "ml" }, "vol", 0.001), (new[] { "l", "liter", "litre", "liters", "litres" }, "vol", 1), (new[] { "gal", "gallon", "gallons" }, "vol", 3.785411784), (new[] { "cup", "cups" }, "vol", 0.2365882365),
        (new[] { "b", "byte", "bytes" }, "data", 1), (new[] { "kb" }, "data", 1024), (new[] { "mb" }, "data", 1048576), (new[] { "gb" }, "data", 1073741824), (new[] { "tb" }, "data", 1099511627776),
        (new[] { "s", "sec", "secs", "second", "seconds" }, "time", 1), (new[] { "min", "mins", "minute", "minutes" }, "time", 60), (new[] { "h", "hr", "hrs", "hour", "hours" }, "time", 3600), (new[] { "day", "days" }, "time", 86400), (new[] { "week", "weeks" }, "time", 604800),
        (new[] { "kmh", "km/h", "kph" }, "speed", 1 / 3.6), (new[] { "mph" }, "speed", 0.44704), (new[] { "m/s", "mps" }, "speed", 1),
    };

    public static bool TryConvert(string input, out string result)
    {
        result = "";
        var parts = input.Trim().ToLowerInvariant().Replace("°", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // forms: "10 km to mi" or "10km to mi"
        if (parts.Length == 3 && parts[1] is "to" or "in") parts = SplitNumber(parts[0]).Concat(parts[1..]).ToArray();
        if (parts.Length != 4 || parts[2] is not ("to" or "in")) return false;
        if (!double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
        string from = parts[1], to = parts[3];

        if (Temp(from) is { } tf && Temp(to) is { } tt)
        {
            var c = tf switch { 'f' => (value - 32) * 5 / 9, 'k' => value - 273.15, _ => value };
            var r = tt switch { 'f' => c * 9 / 5 + 32, 'k' => c + 273.15, _ => c };
            result = $"{Calculator.Format(Math.Round(r, 2))} °{char.ToUpper(tt)}";
            return true;
        }
        var a = Table.FirstOrDefault(u => u.Names.Contains(from));
        var b = Table.FirstOrDefault(u => u.Names.Contains(to));
        if (a.Names is null || b.Names is null || a.Group != b.Group) return false;
        result = $"{Calculator.Format(Math.Round(value * a.Factor / b.Factor, 6))} {to}";
        return true;
    }

    private static char? Temp(string u) => u switch { "c" or "celsius" => 'c', "f" or "fahrenheit" => 'f', "k" or "kelvin" => 'k', _ => null };

    private static string[] SplitNumber(string s)
    {
        var i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or ',' or '-')) i++;
        return i == 0 || i == s.Length ? new[] { s } : new[] { s[..i], s[i..] };
    }
}
