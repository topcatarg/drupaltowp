using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Helpers;

internal static class SlugHelpers
{

    public static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "category";

        return name.ToLowerInvariant()
                   .Replace(" ", "-")
                   .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
                   .Replace("ñ", "n").Replace("ü", "u")
                   .Replace("'", "").Replace("\"", "").Replace("/", "-").Replace("\\", "-")
                   .Replace("&", "y").Replace("%", "").Replace("#", "").Replace("@", "")
                   .Replace("!", "").Replace("?", "").Replace("(", "").Replace(")", "")
                   .Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "")
                   .Replace(",", "").Replace(".", "").Replace(";", "").Replace(":", "")
                   .Replace("+", "").Replace("=", "").Replace("*", "").Replace("^", "")
                   .Replace("$", "").Replace("|", "").Replace("<", "").Replace(">", "")
                   .Replace("~", "").Replace("`", "");
    }
}
