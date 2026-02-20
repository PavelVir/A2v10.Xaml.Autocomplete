// Copyright © 2026 Virich Pavlo. All rights reserved.

using Xunit;

namespace A2v10XamlAutocomplete.Tests;

public class XmlContextParserTests
{
    #region None / edge cases

    [Fact]
    public void NullInput_ReturnsNone()
    {
        var ctx = XmlContextParser.Parse(null, 0);
        Assert.Equal(XmlContextType.None, ctx.Type);
    }

    [Fact]
    public void EmptyInput_ReturnsNone()
    {
        var ctx = XmlContextParser.Parse("", 0);
        Assert.Equal(XmlContextType.None, ctx.Type);
    }

    [Fact]
    public void PositionZero_ReturnsContent()
    {
        var ctx = XmlContextParser.Parse("<Page />", 0);
        Assert.Equal(XmlContextType.Content, ctx.Type);
    }

    [Fact]
    public void NegativePosition_ReturnsNone()
    {
        var ctx = XmlContextParser.Parse("<Page />", -1);
        Assert.Equal(XmlContextType.None, ctx.Type);
    }

    [Fact]
    public void PositionBeyondLength_ReturnsNone()
    {
        var ctx = XmlContextParser.Parse("<Page />", 100);
        Assert.Equal(XmlContextType.None, ctx.Type);
    }

    #endregion

    #region TagName

    [Fact]
    public void AfterOpenAngleBracket_ReturnsTagName()
    {
        // <|
        var text = "<";
        var ctx = XmlContextParser.Parse(text, 1);
        Assert.Equal(XmlContextType.TagName, ctx.Type);
        Assert.Equal("", ctx.PartialInput);
    }

    [Fact]
    public void PartialTagName_ReturnsTagNameWithPartial()
    {
        // <But|
        var text = "<But";
        var ctx = XmlContextParser.Parse(text, 4);
        Assert.Equal(XmlContextType.TagName, ctx.Type);
        Assert.Equal("But", ctx.PartialInput);
    }

    [Fact]
    public void FullTagName_ReturnsTagName()
    {
        // <Button|  (no space after, so still typing tag name)
        var text = "<Button";
        var ctx = XmlContextParser.Parse(text, 7);
        Assert.Equal(XmlContextType.TagName, ctx.Type);
        Assert.Equal("Button", ctx.PartialInput);
    }

    [Fact]
    public void PrefixedTagName_StripsPrefix()
    {
        // <a2:But|
        var text = "<a2:But";
        var ctx = XmlContextParser.Parse(text, 7);
        Assert.Equal(XmlContextType.TagName, ctx.Type);
        Assert.Equal("But", ctx.PartialInput);
    }

    #endregion

    #region ClosingTag

    [Fact]
    public void ClosingTagSlash_ReturnsClosingTag()
    {
        // </|
        var text = "<Page></";
        var ctx = XmlContextParser.Parse(text, 8);
        Assert.Equal(XmlContextType.ClosingTag, ctx.Type);
        Assert.Equal("", ctx.PartialInput);
    }

    [Fact]
    public void ClosingTagPartial_ReturnsClosingTagWithPartial()
    {
        // </Pa|
        var text = "<Page></Pa";
        var ctx = XmlContextParser.Parse(text, 10);
        Assert.Equal(XmlContextType.ClosingTag, ctx.Type);
        Assert.Equal("Pa", ctx.PartialInput);
    }

    #endregion

    #region AttributeName

    [Fact]
    public void AfterTagNameAndSpace_ReturnsAttributeName()
    {
        // <Button |
        var text = "<Button ";
        var ctx = XmlContextParser.Parse(text, 8);
        Assert.Equal(XmlContextType.AttributeName, ctx.Type);
        Assert.Equal("Button", ctx.CurrentTag);
        Assert.Equal("", ctx.PartialInput);
    }

    [Fact]
    public void PartialAttributeName_ReturnsAttributeNameWithPartial()
    {
        // <Button Wi|
        var text = "<Button Wi";
        var ctx = XmlContextParser.Parse(text, 10);
        Assert.Equal(XmlContextType.AttributeName, ctx.Type);
        Assert.Equal("Button", ctx.CurrentTag);
        Assert.Equal("Wi", ctx.PartialInput);
    }

    [Fact]
    public void PrefixedTag_AttributeName_StripsPrefix()
    {
        // <a2:Button |
        var text = "<a2:Button ";
        var ctx = XmlContextParser.Parse(text, 11);
        Assert.Equal(XmlContextType.AttributeName, ctx.Type);
        Assert.Equal("Button", ctx.CurrentTag);
    }

    [Fact]
    public void ExistingAttributes_AreDetected()
    {
        // <Button Width="100" |
        var text = "<Button Width=\"100\" ";
        var ctx = XmlContextParser.Parse(text, 20);
        Assert.Equal(XmlContextType.AttributeName, ctx.Type);
        Assert.Contains("Width", ctx.ExistingAttributes);
    }

    [Fact]
    public void MultipleExistingAttributes_AreAllDetected()
    {
        // <Button Width="100" Height="50" |
        var text = "<Button Width=\"100\" Height=\"50\" ";
        var ctx = XmlContextParser.Parse(text, 32);
        Assert.Equal(XmlContextType.AttributeName, ctx.Type);
        Assert.Contains("Width", ctx.ExistingAttributes);
        Assert.Contains("Height", ctx.ExistingAttributes);
    }

    #endregion

    #region AttributeValue

    [Fact]
    public void InsideAttributeValue_ReturnsAttributeValue()
    {
        // <Button Width="|
        var text = "<Button Width=\"";
        var ctx = XmlContextParser.Parse(text, 15);
        Assert.Equal(XmlContextType.AttributeValue, ctx.Type);
        Assert.Equal("Button", ctx.CurrentTag);
        Assert.Equal("Width", ctx.CurrentAttribute);
        Assert.Equal("", ctx.PartialInput);
    }

    [Fact]
    public void PartialAttributeValue_ReturnsCorrectPartial()
    {
        // <Button Width="10|
        var text = "<Button Width=\"10";
        var ctx = XmlContextParser.Parse(text, 17);
        Assert.Equal(XmlContextType.AttributeValue, ctx.Type);
        Assert.Equal("10", ctx.PartialInput);
    }

    [Fact]
    public void AttributeValueWithSingleQuotes_Works()
    {
        // <Button Width='|
        var text = "<Button Width='";
        var ctx = XmlContextParser.Parse(text, 15);
        Assert.Equal(XmlContextType.AttributeValue, ctx.Type);
        Assert.Equal("Width", ctx.CurrentAttribute);
    }

    #endregion

    #region ElementProperty

    [Fact]
    public void ElementProperty_AfterDot_ReturnsElementProperty()
    {
        // <Button.|
        var text = "<Page><Button.";
        var ctx = XmlContextParser.Parse(text, 14);
        Assert.Equal(XmlContextType.ElementProperty, ctx.Type);
        Assert.Equal("Button", ctx.CurrentTag);
        Assert.Equal("", ctx.PartialInput);
    }

    [Fact]
    public void ElementProperty_WithPartial_ReturnsCorrectPartial()
    {
        // <Button.Too|
        var text = "<Page><Button.Too";
        var ctx = XmlContextParser.Parse(text, 17);
        Assert.Equal(XmlContextType.ElementProperty, ctx.Type);
        Assert.Equal("Button", ctx.CurrentTag);
        Assert.Equal("Too", ctx.PartialInput);
    }

    [Fact]
    public void ElementProperty_WithPrefix_StripsPrefix()
    {
        // <a2:Button.|
        var text = "<Page><a2:Button.";
        var ctx = XmlContextParser.Parse(text, 17);
        Assert.Equal(XmlContextType.ElementProperty, ctx.Type);
        Assert.Equal("Button", ctx.CurrentTag);
    }

    #endregion

    #region Content

    [Fact]
    public void BetweenTags_ReturnsContent()
    {
        // <Page>|
        var text = "<Page>";
        var ctx = XmlContextParser.Parse(text, 6);
        Assert.Equal(XmlContextType.Content, ctx.Type);
    }

    [Fact]
    public void ContentWithPartialText_ReturnsContentWithPartial()
    {
        // <Page>Bu|
        var text = "<Page>Bu";
        var ctx = XmlContextParser.Parse(text, 8);
        Assert.Equal(XmlContextType.Content, ctx.Type);
        Assert.Equal("Bu", ctx.PartialInput);
    }

    #endregion

    #region Comment / CDATA

    [Fact]
    public void InsideComment_ReturnsNone()
    {
        // <!-- comm|
        var text = "<!-- comm";
        var ctx = XmlContextParser.Parse(text, 9);
        Assert.Equal(XmlContextType.None, ctx.Type);
    }

    [Fact]
    public void AfterClosedComment_ReturnsContent()
    {
        // <!-- comment -->|
        var text = "<!-- comment -->";
        var ctx = XmlContextParser.Parse(text, 16);
        Assert.Equal(XmlContextType.Content, ctx.Type);
    }

    [Fact]
    public void InsideCDATA_ReturnsNone()
    {
        // <![CDATA[ some data |
        var text = "<![CDATA[ some data ";
        var ctx = XmlContextParser.Parse(text, 20);
        Assert.Equal(XmlContextType.None, ctx.Type);
    }

    [Fact]
    public void AfterClosedCDATA_ReturnsContent()
    {
        // <![CDATA[data]]>|
        var text = "<![CDATA[data]]>";
        var ctx = XmlContextParser.Parse(text, 16);
        Assert.Equal(XmlContextType.Content, ctx.Type);
    }

    #endregion

    #region ParentTag

    [Fact]
    public void NestedTags_CorrectParentTag()
    {
        // <Page><Button>|
        //   ParentTag of content after <Button> should be "Button"
        var text = "<Page><Button>";
        var ctx = XmlContextParser.Parse(text, 14);
        Assert.Equal(XmlContextType.Content, ctx.Type);
        Assert.Equal("Button", ctx.ParentTag);
    }

    [Fact]
    public void DeeplyNested_CorrectParentTag()
    {
        // <Page><Grid><Button |
        var text = "<Page><Grid><Button ";
        var ctx = XmlContextParser.Parse(text, 20);
        Assert.Equal(XmlContextType.AttributeName, ctx.Type);
        Assert.Equal("Grid", ctx.ParentTag);
    }

    [Fact]
    public void ClosedSibling_CorrectParentTag()
    {
        // <Page><Button /><|
        //   ParentTag should be "Page"
        var text = "<Page><Button /><";
        var ctx = XmlContextParser.Parse(text, 17);
        Assert.Equal(XmlContextType.TagName, ctx.Type);
        Assert.Equal("Page", ctx.ParentTag);
    }

    [Fact]
    public void ClosedPair_CorrectParentTag()
    {
        // <Page><Button></Button><|
        //   ParentTag should be "Page"
        var text = "<Page><Button></Button><";
        var ctx = XmlContextParser.Parse(text, 24);
        Assert.Equal(XmlContextType.TagName, ctx.Type);
        Assert.Equal("Page", ctx.ParentTag);
    }

    #endregion

    #region A2v10 prefix detection

    [Fact]
    public void DefaultNamespace_PrefixIsNull()
    {
        var text = "<Page xmlns=\"clr-namespace:A2v10.Xaml\">\n<Button ";
        var ctx = XmlContextParser.Parse(text, text.Length);
        Assert.Null(ctx.A2v10Prefix);
    }

    [Fact]
    public void PrefixedNamespace_ReturnsPrefix()
    {
        var text = "<Page xmlns:a2=\"clr-namespace:A2v10.Xaml\">\n<a2:Button ";
        var ctx = XmlContextParser.Parse(text, text.Length);
        Assert.Equal("a2", ctx.A2v10Prefix);
    }

    [Fact]
    public void NoA2v10Namespace_PrefixIsNull()
    {
        var text = "<Page xmlns=\"http://schemas.example.com\">\n<Button ";
        var ctx = XmlContextParser.Parse(text, text.Length);
        Assert.Null(ctx.A2v10Prefix);
    }

    #endregion
}
