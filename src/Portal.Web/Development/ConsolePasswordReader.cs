using System;
using System.Text;

namespace Portal.Web.Development;

public static class ConsolePasswordReader
{
    public static string Read(string message)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "Ejecuta este proceso desde una consola interactiva.");
        }

        Console.Write(message);

        var password = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();

                throw new OperationCanceledException(
                    "Se canceló la entrada de la contraseña.");
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }
}