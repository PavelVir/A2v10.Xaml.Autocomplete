// Copyright © 2026 Virich Pavlo. All rights reserved.

using Xunit;

namespace A2v10XamlAutocomplete.Tests;

public class CommitCalculatorTests
{
    #region Tag

    [Theory]
    [InlineData('\0')]
    public void Tag_TabOrEnter_InsertsNameWithSpace(char typedChar)
    {
        var result = CommitCalculator.Tag("Button", typedChar);

        Assert.True(result.Handled);
        Assert.Equal("Button ", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    [Fact]
    public void Tag_GreaterThan_InsertsNameWithClosingBracket()
    {
        var result = CommitCalculator.Tag("Button", '>');

        Assert.True(result.Handled);
        Assert.Equal("Button>", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    [Fact]
    public void Tag_Slash_InsertsNameWithSlash()
    {
        var result = CommitCalculator.Tag("Button", '/');

        Assert.True(result.Handled);
        Assert.Equal("Button/", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('=')]
    [InlineData('\'')]
    public void Tag_OtherChars_ReturnsUnhandled(char typedChar)
    {
        var result = CommitCalculator.Tag("Button", typedChar);

        Assert.False(result.Handled);
    }

    #endregion

    #region ClosingTag

    [Theory]
    [InlineData('\0')]
    public void ClosingTag_TabOrEnter_InsertsNameWithClosingBracket(char typedChar)
    {
        var result = CommitCalculator.ClosingTag("Page", typedChar);

        Assert.True(result.Handled);
        Assert.Equal("Page>", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    [Fact]
    public void ClosingTag_GreaterThan_InsertsNameWithoutDuplicateBracket()
    {
        var result = CommitCalculator.ClosingTag("Page", '>');

        Assert.True(result.Handled);
        Assert.Equal("Page", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    #endregion

    #region Attribute

    [Fact]
    public void Attribute_SimpleProperty_InsertsNameWithEquals()
    {
        var result = CommitCalculator.Attribute("Width");

        Assert.True(result.Handled);
        Assert.Equal("Width=", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    [Fact]
    public void Attribute_DottedProperty_InsertsNameWithEquals()
    {
        var result = CommitCalculator.Attribute("Grid.Row");

        Assert.True(result.Handled);
        Assert.Equal("Grid.Row=", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    #endregion

    #region Value

    [Theory]
    [InlineData('\0')]
    public void Value_TabOrEnter_InsertsValueWithClosingQuote(char typedChar)
    {
        var result = CommitCalculator.Value("True", typedChar);

        Assert.True(result.Handled);
        Assert.Equal("True\"", result.Text);
        Assert.Equal(-1, result.CaretOffset);
    }

    [Fact]
    public void Value_Quote_ReturnsUnhandled()
    {
        var result = CommitCalculator.Value("True", '"');

        Assert.False(result.Handled);
    }

    #endregion

    #region Comment

    [Fact]
    public void Comment_InsertsCommentBodyWithCaretInside()
    {
        var result = CommitCalculator.Comment();

        Assert.True(result.Handled);
        Assert.Equal("!--  -->", result.Text);
        Assert.Equal(4, result.CaretOffset);
        // Caret lands between "!-- " and " -->"
        Assert.Equal("!-- ", result.Text.Substring(0, result.CaretOffset));
    }

    #endregion

    #region CData

    [Fact]
    public void CData_InsertsCDataBodyWithCaretInside()
    {
        var result = CommitCalculator.CData();

        Assert.True(result.Handled);
        Assert.Equal("![CDATA[]]>", result.Text);
        Assert.Equal(8, result.CaretOffset);
        // Caret lands between "![CDATA[" and "]]>"
        Assert.Equal("![CDATA[", result.Text.Substring(0, result.CaretOffset));
    }

    #endregion
}
