using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Datadog.Trace.Util
{
    internal static class UriHelpers
    {
        public static string CleanUri(Uri uri, bool removeScheme, bool tryRemoveIds)
        {
            var path = GetRelativeUrl(uri, tryRemoveIds);

            if (removeScheme)
            {
                // keep only host and path.
                // remove scheme, userinfo, query, and fragment.
                return $"{uri.Authority}{path}";
            }

            // keep only scheme, authority, and path.
            // remove userinfo, query, and fragment.
            return $"{uri.Scheme}{Uri.SchemeDelimiter}{uri.Authority}{path}";
        }

        public static string GetRelativeUrl(Uri uri, bool tryRemoveIds)
        {
            return GetRelativeUrl(uri.AbsolutePath, tryRemoveIds);
        }

        public static string GetRelativeUrl(string uri, bool tryRemoveIds)
        {
            // try to remove segments that look like ids
            return tryRemoveIds ? RemoveIds(uri) : uri;
        }

        public static string RemoveIds(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || (absolutePath.Length == 1 && absolutePath[0] == '/'))
            {
                return absolutePath;
            }

            // Sanitized url will be at worse as long as the original
            var sb = StringBuilderCache.Acquire(absolutePath.Length);

#if NETCOREAPP
            ReadOnlySpan<char> absPath = absolutePath.AsSpan();
            int previousIndex = 0;
            int index;

            do
            {
                ReadOnlySpan<char> nStart = absPath.Slice(previousIndex);
                index = nStart.IndexOf('/');

                // replace path segments that look like numbers or guids
                if (ShouldReplace(absPath))
                {
                    sb.Append('?');
                }
                else
                {
                    ReadOnlySpan<char> segment = index == -1 ? nStart : nStart.Slice(0, index);
                    sb.Append(segment);
                }

                if (index != -1)
                {
                    sb.Append('/');
                }

                previousIndex += index + 1;
            }
            while (index != -1);

            return StringBuilderCache.GetStringAndRelease(sb);
#else
            int previousIndex = 0;
            int index = 0;

            do
            {
                index = absolutePath.IndexOf('/', previousIndex);
                string segment;
                int length;

                if (index == -1)
                {
                    // Last segment
                    length = absolutePath.Length - previousIndex;
                }
                else
                {
                    length = index - previousIndex;
                }

                if (ShouldReplace(absolutePath, previousIndex, length))
                {
                    // replace path segments that look like numbers or guids
                    segment = "?";
                }
                else
                {
                    segment = absolutePath.Substring(previousIndex, length);
                }

                sb.Append(segment);

                if (index != -1)
                {
                    sb.Append("/");
                }

                previousIndex = index + 1;
            }
            while (index != -1);

            return StringBuilderCache.GetStringAndRelease(sb);
#endif
        }

#if NETCOREAPP
        internal static bool ShouldReplace(ReadOnlySpan<char> path)
        {
            if (path.Length == 0)
            {
                return false;
            }

            foreach (var c in path)
            {
                switch (c)
                {
                    case >= '0' and <= '9':
                        continue;
                    case >= 'a' and <= 'f':
                    case >= 'A' and <= 'F':
                        if (path.Length < 16)
                        {
                            // don't be too aggresive replacing
                            // short hex segments like "/a" or "/cab",
                            // they are likely not ids
                            return false;
                        }

                        continue;
                    case '-':
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }
#else
        internal static bool ShouldReplace(string path, int startIndex, int length)
        {
            if (length == 0)
            {
                return false;
            }

            int lastIndex = startIndex + length;

            for (int index = startIndex; index < lastIndex && index < path.Length; index++)
            {
                char c = path[index];

                switch (c)
                {
                    case >= '0' and <= '9':
                        continue;
                    case >= 'a' and <= 'f':
                    case >= 'A' and <= 'F':
                        if (path.Length < 16)
                        {
                            // don't be too aggresive replacing
                            // short hex segments like "/a" or "/cab",
                            // they are likely not ids
                            return false;
                        }

                        continue;
                    case '-':
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }
#endif

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsAGuid(string segment, string format) => Guid.TryParseExact(segment, format, out _);

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsAGuid(ReadOnlySpan<char> segment, string format) => Guid.TryParseExact(segment, format, out _);
#endif

        /// <summary>
        /// Provide a cached reusable instance of stringbuilder per thread.
        /// </summary>
        /// <remarks>
        /// Based on https://source.dot.net/#System.Private.CoreLib/StringBuilderCache.cs,a6dbe82674916ac0
        /// </remarks>
        internal static class StringBuilderCache
        {
            internal const int MaxBuilderSize = 360;

            [ThreadStatic]
            private static StringBuilder _cachedInstance;

            public static StringBuilder Acquire(int capacity)
            {
                if (capacity <= MaxBuilderSize)
                {
                    StringBuilder sb = _cachedInstance;
                    if (sb != null)
                    {
                        // Avoid stringbuilder block fragmentation by getting a new StringBuilder
                        // when the requested size is larger than the current capacity
                        if (capacity <= sb.Capacity)
                        {
                            _cachedInstance = null;
                            sb.Clear();
                            return sb;
                        }
                    }
                }

                return new StringBuilder(capacity);
            }

            public static string GetStringAndRelease(StringBuilder sb)
            {
                string result = sb.ToString();
                if (sb.Capacity <= MaxBuilderSize)
                {
                    _cachedInstance = sb;
                }

                return result;
            }
        }
    }
}
