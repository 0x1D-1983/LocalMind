using System.Text;

namespace LocalMind.Agent;

/// <summary>
/// When the model is instructed to output a JSON object, stream only the
/// human-facing <c>answer</c> string.
/// </summary>
internal sealed class JsonAnswerStreamExtractor
{
    private enum State
    {
        SeekKey,
        SeekColon,
        SeekValueStart,
        InValue,
        Escape,
        Unicode1,
        Unicode2,
        Unicode3,
        Unicode4
    }

    private static ReadOnlySpan<char> Key => "\"answer\"";

    private State state = State.SeekKey;
    private int keyMatchIdx;
    private int unicode;

    public IEnumerable<string> Push(string input)
    {
        if (string.IsNullOrEmpty(input))
            yield break;

        var sb = new StringBuilder();

        foreach (var ch in input)
        {
            switch (state)
            {
                case State.SeekKey:
                {
                    if (ch == Key[keyMatchIdx])
                    {
                        keyMatchIdx++;
                        if (keyMatchIdx == Key.Length)
                        {
                            state = State.SeekColon;
                            keyMatchIdx = 0;
                        }
                    }
                    else
                    {
                        keyMatchIdx = ch == Key[0] ? 1 : 0;
                    }

                    break;
                }

                case State.SeekColon:
                {
                    if (char.IsWhiteSpace(ch))
                        break;
                    if (ch == ':')
                        state = State.SeekValueStart;
                    else
                        state = State.SeekKey;
                    break;
                }

                case State.SeekValueStart:
                {
                    if (char.IsWhiteSpace(ch))
                        break;
                    if (ch == '"')
                        state = State.InValue;
                    else
                        state = State.SeekKey;
                    break;
                }

                case State.InValue:
                {
                    if (ch == '\\')
                    {
                        state = State.Escape;
                        break;
                    }

                    if (ch == '"')
                    {
                        state = State.SeekKey;
                        break;
                    }

                    sb.Append(ch);
                    break;
                }

                case State.Escape:
                {
                    state = State.InValue;
                    switch (ch)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            unicode = 0;
                            state = State.Unicode1;
                            break;
                        default:
                            sb.Append(ch);
                            break;
                    }

                    break;
                }

                case State.Unicode1:
                case State.Unicode2:
                case State.Unicode3:
                case State.Unicode4:
                {
                    if (!TryHex(ch, out var val))
                    {
                        state = State.InValue;
                        break;
                    }

                    unicode = (unicode << 4) | val;

                    state = state switch
                    {
                        State.Unicode1 => State.Unicode2,
                        State.Unicode2 => State.Unicode3,
                        State.Unicode3 => State.Unicode4,
                        _ => State.InValue
                    };

                    if (state == State.InValue)
                        sb.Append((char)unicode);

                    break;
                }
            }

            if (sb.Length >= 256)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static bool TryHex(char ch, out int value)
    {
        if (ch is >= '0' and <= '9')
        {
            value = ch - '0';
            return true;
        }
        if (ch is >= 'a' and <= 'f')
        {
            value = 10 + (ch - 'a');
            return true;
        }
        if (ch is >= 'A' and <= 'F')
        {
            value = 10 + (ch - 'A');
            return true;
        }

        value = 0;
        return false;
    }
}
