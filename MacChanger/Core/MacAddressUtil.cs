using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MacChanger.Core
{
    /// <summary>MAC 주소 문자열 정규화 / 서식 / 검증 / 랜덤 생성.</summary>
    public static class MacAddressUtil
    {
        /// <summary>
        /// 입력에서 구분자(- : . 공백)를 제거하고 12자리 대문자 16진수로 정규화한다. 형식이 잘못되면 null.
        /// </summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            StringBuilder sb = new StringBuilder(12);
            foreach (char c in input.Trim())
            {
                if (c == '-' || c == ':' || c == '.' || c == ' ' || c == '\t') continue;
                if (!IsHexChar(c)) return null;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.Length == 12 ? sb.ToString() : null;
        }

        public static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        /// <summary>12자리 hex를 "AA-BB-CC-DD-EE-FF" 형태로 표시한다. 정규화 불가능하면 입력을 그대로 돌려준다.</summary>
        public static string Format(string mac)
        {
            string n = Normalize(mac);
            if (n == null) return mac ?? string.Empty;
            StringBuilder sb = new StringBuilder(17);
            for (int i = 0; i < 12; i += 2)
            {
                if (i > 0) sb.Append('-');
                sb.Append(n, i, 2);
            }
            return sb.ToString();
        }

        /// <summary>바이트 배열(최소 6바이트)을 12자리 대문자 hex로 변환한다.</summary>
        public static string FromBytes(byte[] bytes, int length)
        {
            if (bytes == null || length < 6 || bytes.Length < 6) return null;
            StringBuilder sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++)
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static int FirstOctet(string mac)
        {
            string n = Normalize(mac);
            if (n == null) throw new ArgumentException("MAC 형식이 올바르지 않습니다.", "mac");
            return int.Parse(n.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        /// <summary>첫 옥텟 bit0 == 0 → 유니캐스트</summary>
        public static bool IsUnicast(string mac)
        {
            return (FirstOctet(mac) & 0x01) == 0;
        }

        /// <summary>첫 옥텟 bit1 == 1 → 로컬 관리 주소(Locally Administered)</summary>
        public static bool IsLocallyAdministered(string mac)
        {
            return (FirstOctet(mac) & 0x02) != 0;
        }

        /// <summary>00-00-00-00-00-00</summary>
        public static bool IsAllZero(string mac)
        {
            return Normalize(mac) == "000000000000";
        }

        /// <summary>FF-FF-FF-FF-FF-FF (브로드캐스트)</summary>
        public static bool IsAllFF(string mac)
        {
            return Normalize(mac) == "FFFFFFFFFFFF";
        }

        /// <summary>왼쪽에서 두 번째 16진 자리(첫 옥텟의 하위 니블)</summary>
        public static char SecondDigit(string mac)
        {
            string n = Normalize(mac);
            if (n == null) throw new ArgumentException("MAC 형식이 올바르지 않습니다.", "mac");
            return n[1];
        }

        /// <summary>무선 규칙: 두 번째 자리가 2/6/A/E (유니캐스트 + 로컬 관리)</summary>
        public static bool MatchesWirelessRule(string mac)
        {
            return "26AE".IndexOf(SecondDigit(mac)) >= 0;
        }

        /// <summary>
        /// 랜덤 MAC 생성 (12자리 모두 0~F 범위에서 무작위, 첫 자리도 고정하지 않음).
        /// wireless == true  : 왼쪽에서 두 번째 자리만 2/6/A/E 중 하나 → X2/X6/XA/XE-XX-XX-XX-XX-XX (유니캐스트·로컬관리 보장)
        /// wireless == false : 왼쪽에서 두 번째 자리만 짝수(0/2/4/6/8/A/C/E) → 유니캐스트 보장, 00-00-00-00-00-00 과 FF-FF-FF-FF-FF-FF 는 제외
        /// </summary>
        public static string GenerateRandom(bool wireless)
        {
            const string hex = "0123456789ABCDEF";
            string secondChoices = wireless ? "26AE" : "02468ACE";
            byte[] rnd = new byte[12];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    rng.GetBytes(rnd);
                    char[] c = new char[12];
                    for (int i = 0; i < 12; i++) c[i] = hex[rnd[i] & 0x0F];
                    c[1] = secondChoices[rnd[1] & (secondChoices.Length - 1)];   // 4 또는 8개 중 균등 선택
                    string mac = new string(c);
                    if (mac == "000000000000" || mac == "FFFFFFFFFFFF") continue;
                    return mac;
                }
            }
            throw new InvalidOperationException("랜덤 MAC 생성에 실패했습니다.");
        }
    }
}
