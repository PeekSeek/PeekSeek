namespace PeekSeek.Core;

internal static class Ex {
    public static string Translate(this string str) {
        return PeekSeek.Instance.Translate(str);
    }

    public static int ToFemaleSalary(this int salary) {
        return (int)(salary * 0.8);
    }
}
