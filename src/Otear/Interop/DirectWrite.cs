using System.Runtime.InteropServices;

namespace Otear.Interop;

/// <summary>An installed font family, named as DirectWrite (and so libass, mpv's subtitle renderer) knows it.</summary>
public sealed record SystemFontFamily(string Name, bool IsSymbol);

/// <summary>Minimal, AOT-safe DirectWrite calls (vtable function pointers) to list the installed font families.</summary>
internal static unsafe partial class DirectWrite
{
    // dwrite.h
    private static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private const int DWRITE_FACTORY_TYPE_SHARED = 0;
    private const int FactoryGetSystemFontCollection = 3;   // IDWriteFactory
    private const int CollectionGetFontFamilyCount = 3;     // IDWriteFontCollection
    private const int CollectionGetFontFamily = 4;
    private const int FamilyGetFamilyNames = 6;             // IDWriteFontFamily (after IDWriteFontList's 3)
    private const int FamilyGetFirstMatchingFont = 7;
    private const int FontIsSymbolFont = 7;                 // IDWriteFont
    private const int FontHasCharacter = 12;
    private const int StringsGetCount = 3;                  // IDWriteLocalizedStrings
    private const int StringsFindLocaleName = 4;
    private const int StringsGetStringLength = 7;
    private const int StringsGetString = 8;

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int factoryType, in Guid iid, out nint factory);

    private static void** Vtbl(nint obj) => *(void***)obj;

    /// <summary>
    /// The installed font families (including per-user fonts), sorted by name. Symbol and icon fonts are flagged
    /// so their names are not drawn in their own glyphs.
    /// </summary>
    public static List<SystemFontFamily> GetSystemFontFamilies()
    {
        var result = new List<SystemFontFamily>();
        Marshal.ThrowExceptionForHR(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, IID_IDWriteFactory, out nint factory));
        nint collection = 0;
        try
        {
            var getCollection = (delegate* unmanaged[Stdcall]<nint, nint*, int, int>)Vtbl(factory)[FactoryGetSystemFontCollection];
            Marshal.ThrowExceptionForHR(getCollection(factory, &collection, 1 /* check for newly installed fonts */));

            uint count = ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(collection)[CollectionGetFontFamilyCount])(collection);
            var getFamily = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtbl(collection)[CollectionGetFontFamily];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                nint family = 0;
                if (getFamily(collection, i, &family) < 0 || family == 0) continue;
                try
                {
                    string? name = FamilyName(family);
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                        result.Add(new SystemFontFamily(name, IsSymbolFamily(family)));
                }
                finally
                {
                    Marshal.Release(family);
                }
            }
        }
        finally
        {
            if (collection != 0) Marshal.Release(collection);
            Marshal.Release(factory);
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>The family's English name (the one most subtitle files and players use), else its first name.</summary>
    private static string? FamilyName(nint family)
    {
        nint names = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtbl(family)[FamilyGetFamilyNames])(family, &names) < 0 || names == 0)
            return null;
        try
        {
            uint index = 0;
            int exists = 0;
            fixed (char* enUs = "en-us")
                ((delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)Vtbl(names)[StringsFindLocaleName])(names, enUs, &index, &exists);
            if (exists == 0)
            {
                index = 0;
                if (((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(names)[StringsGetCount])(names) == 0) return null;
            }
            uint length = 0;
            if (((delegate* unmanaged[Stdcall]<nint, uint, uint*, int>)Vtbl(names)[StringsGetStringLength])(names, index, &length) < 0) return null;
            var buffer = new char[length + 1];
            fixed (char* p = buffer)
            {
                if (((delegate* unmanaged[Stdcall]<nint, uint, char*, uint, int>)Vtbl(names)[StringsGetString])(names, index, p, length + 1) < 0)
                    return null;
            }
            return new string(buffer, 0, (int)length).Trim();
        }
        finally
        {
            Marshal.Release(names);
        }
    }

    /// <summary>True for symbol fonts (Wingdings…) and icon fonts without Latin letters (Segoe Fluent Icons…).</summary>
    private static bool IsSymbolFamily(nint family)
    {
        nint font = 0;
        // DWRITE_FONT_WEIGHT_NORMAL, DWRITE_FONT_STRETCH_NORMAL, DWRITE_FONT_STYLE_NORMAL
        var firstMatching = (delegate* unmanaged[Stdcall]<nint, int, int, int, nint*, int>)Vtbl(family)[FamilyGetFirstMatchingFont];
        if (firstMatching(family, 400, 5, 0, &font) < 0 || font == 0) return false;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, int>)Vtbl(font)[FontIsSymbolFont])(font) != 0) return true;
            int hasA = 0;
            ((delegate* unmanaged[Stdcall]<nint, uint, int*, int>)Vtbl(font)[FontHasCharacter])(font, 'A', &hasA);
            return hasA == 0;
        }
        finally
        {
            Marshal.Release(font);
        }
    }
}
