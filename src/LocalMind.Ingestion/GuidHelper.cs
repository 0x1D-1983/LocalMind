using System.Security.Cryptography;
using System.Text;

namespace LocalMind.Ingestion;

public static class GuidHelper
{
    public static Guid CreateDeterministicGuid(string documentLabel, int index) =>
        new Guid(MD5.Create().ComputeHash(Encoding.Unicode.GetBytes($"{documentLabel}::{index}")));
}