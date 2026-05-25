CREATE TABLE dbo.UnicodeRows (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Label NVARCHAR(200) NOT NULL
);

-- Each row exercises a different Unicode hazard. Values are written with NCHAR(...)
-- so the file stays ASCII and the test is independent of editor encoding.
INSERT INTO dbo.UnicodeRows (Label) VALUES
    -- Emoji ZWJ family sequence: man + ZWJ + woman + ZWJ + girl + ZWJ + boy
    (NCHAR(0xD83D) + NCHAR(0xDC68) + NCHAR(0x200D) + NCHAR(0xD83D) + NCHAR(0xDC69)
     + NCHAR(0x200D) + NCHAR(0xD83D) + NCHAR(0xDC67) + NCHAR(0x200D) + NCHAR(0xD83D) + NCHAR(0xDC66)),
    -- RTL: Arabic + Hebrew + Latin
    (NCHAR(0x0645) + NCHAR(0x0631) + NCHAR(0x062D) + NCHAR(0x0628) + NCHAR(0x0627)
     + N' ' + NCHAR(0x05E9) + NCHAR(0x05DC) + NCHAR(0x05D5) + NCHAR(0x05DD) + N' world'),
    -- CJK Han + Hiragana + Katakana
    (NCHAR(0x4E2D) + NCHAR(0x6587) + N' / ' + NCHAR(0x65E5) + NCHAR(0x672C) + NCHAR(0x8A9E)),
    -- Combining diacritic: 'a' + combining acute accent (should equal 'a' + U+0301)
    (N'a' + NCHAR(0x0301) + N' vs ' + NCHAR(0x00E1)),
    -- Zero-width joiner between two letters
    (N'A' + NCHAR(0x200D) + N'B'),
    -- Supplementary-plane codepoint: musical G clef U+1D11E as surrogate pair
    (NCHAR(0xD834) + NCHAR(0xDD1E) + N' G clef');
