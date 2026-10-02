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

        public static bool IsAllZero(string mac)
        {
            string n = Normalize(mac);
            return n == "000000000000";
        }

        /// <summary>
        /// 랜덤 MAC 생성. 첫 옥텟은 항상 유니캐스트(bit0=0)·로컬관리(bit1=1).
        /// fixFirstOctetTo02 == true이면 첫 옥텟을 0x02로 고정, false이면 0x02/0x06/0x0A/0x0E 중 무작위.
        /// 결과적으로 왼쪽에서 두 번째 자리는 항상 2/6/A/E 이다.
        /// </summary>
        public static string GenerateRandom(bool fixFirstOctetTo02)
        {
            byte[] b = new byte[6];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(b);
            }
            if (fixFirstOctetTo02)
            {
                b[0] = 0x02;
            }
            else
            {
                byte[] choices = new byte[] { 0x02, 0x06, 0x0A, 0x0E };
                b[0] = choices[b[0] & 0x03];
            }
            return FromBytes(b, 6);
        }
    }
}
