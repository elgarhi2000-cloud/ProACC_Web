using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using BlazorApp1.Data.ProAcc;

namespace BlazorApp1.Services;

public static class JournalCsvImporter
{
    public const long MaxFileSize = 5 * 1024 * 1024;
    public static List<TransEntity> Parse(string csv, IEnumerable<AccEntity> accounts, int glId)
    {
        var allowed = accounts.Where(a => a.Active == true).Select(a => a.ACCID).ToHashSet();
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF')));
        parser.SetDelimiters(";");
        parser.HasFieldsEnclosedInQuotes = true;
        var result = new List<TransEntity>();
        while (!parser.EndOfData)
        {
            var line = parser.LineNumber;
            string[] fields;
            try { fields = parser.ReadFields()!; }
            catch (MalformedLineException) { throw new FormatException($"السطر {line}: تنسيق CSV غير صحيح."); }
            void Require(bool valid, string message)
            { if (!valid) throw new FormatException($"السطر {line}: {message}"); }
            Require(fields.Length == 7, "يجب أن يحتوي على سبعة أعمدة مفصولة بفاصلة منقوطة (;).");
            Require(long.TryParse(fields[0], out var account) && allowed.Contains(account), $"الحساب {fields[0]} غير موجود أو غير نشط.");
            decimal Money(string value)
            {
                Require(decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                    && amount >= 0 && amount <= 9999999999999999.99m && decimal.Round(amount, 2) == amount,
                    "المبلغ يجب أن يكون رقمًا غير سالب بمنزلتين عشريتين كحد أقصى.");
                return amount;
            }
            var debit = Money(fields[2]);
            var credit = Money(fields[3]);
            Require(!(debit > 0 && credit > 0), "لا يجوز الجمع بين مدين ودائن في السطر نفسه.");
            Require(fields[5].Length <= 50, "رقم المستند يتجاوز 50 حرفًا.");
            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(fields[6]))
            {
                Require(DateTime.TryParseExact(fields[6], new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed), "تاريخ المستند غير صحيح؛ استخدم يوم/شهر/سنة.");
                date = parsed;
            }
            Require(result.Count < 10000, "الحد الأقصى للاستيراد 10000 سطر.");
            result.Add(new TransEntity { GLID = glId, ACCID = account, DR = debit, CR = credit,
                TransDesc = fields[4], DocRef = string.IsNullOrWhiteSpace(fields[5]) ? null : fields[5], DocDate = date });
        }
        if (result.Count == 0) throw new FormatException("الملف لا يحتوي على سجلات.");
        return result;
    }
}
