using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class MarkdownDocumentBlockBuilderTests
{
    [Fact]
    public void Build_CreatesHeadingBlocksWithParentHierarchy()
    {
        const string markdown = """
                                # Root
                                intro

                                ## Child
                                body

                                ### Leaf
                                leaf body

                                # Next
                                next body
                                """;

        var blocks = MarkdownDocumentBlockBuilder.Build(markdown);

        blocks.Should().HaveCount(4);
        blocks[0].Heading.Should().Be("Root");
        blocks[0].Level.Should().Be(1);
        blocks[0].ParentHeadings.Should().BeEmpty();
        blocks[0].Content.Should().Be("intro");

        blocks[1].Heading.Should().Be("Child");
        blocks[1].Level.Should().Be(2);
        blocks[1].ParentHeadings.Should().Equal("Root");
        blocks[1].Content.Should().Be("body");

        blocks[2].Heading.Should().Be("Leaf");
        blocks[2].Level.Should().Be(3);
        blocks[2].ParentHeadings.Should().Equal("Root", "Child");
        blocks[2].Content.Should().Be("leaf body");

        blocks[3].Heading.Should().Be("Next");
        blocks[3].Level.Should().Be(1);
        blocks[3].ParentHeadings.Should().BeEmpty();
        blocks[3].Content.Should().Be("next body");
    }

    [Fact]
    public void Build_KeepsMarkdownTableAsTableBlock()
    {
        const string markdown = """
                                # Data

                                | A | B |
                                | - | - |
                                | 1 | 2 |
                                """;

        var blocks = MarkdownDocumentBlockBuilder.Build(markdown);

        var table = blocks.Should().ContainSingle(block => block.Kind == DocumentBlockKind.Table).Subject;
        table.Heading.Should().Be("Data");
        table.Level.Should().Be(1);
        table.Content.Should().Be("""
                                  | A | B |
                                  | - | - |
                                  | 1 | 2 |
                                  """);
    }

    [Fact]
    public void Build_HandlesContentWithoutHeading()
    {
        const string markdown = "alpha\n\nbeta";

        var blocks = MarkdownDocumentBlockBuilder.Build(markdown);

        blocks.Should().ContainSingle();
        blocks[0].Kind.Should().Be(DocumentBlockKind.Text);
        blocks[0].Heading.Should().BeEmpty();
        blocks[0].Level.Should().Be(0);
        blocks[0].ParentHeadings.Should().BeEmpty();
        blocks[0].Content.Should().Be(markdown);
    }

    [Fact]
    public void Build_WhenFenceContainsHeadingLikeLine_KeepsItInCodeBlock()
    {
        const string markdown = """
                                # Dev

                                ```csharp
                                # not a heading
                                Console.WriteLine("hi");
                                ```

                                ## Real
                                body
                                """;

        var blocks = MarkdownDocumentBlockBuilder.Build(markdown);

        blocks.Should().HaveCount(2);
        blocks[0].Kind.Should().Be(DocumentBlockKind.Code);
        blocks[0].Heading.Should().Be("Dev");
        blocks[0].Content.Should().Contain("# not a heading");
        blocks.Should().NotContain(block => block.Heading == "not a heading");
        blocks[1].Kind.Should().Be(DocumentBlockKind.Text);
        blocks[1].Heading.Should().Be("Real");
        blocks[1].ParentHeadings.Should().Equal("Dev");
        blocks[1].Content.Should().Be("body");
    }

    [Fact]
    public void Build_SourceSpansMapBlockContentBackToOriginalMarkdown()
    {
        const string markdown =
            "# Data\r\n\r\nalpha\r\n\r\n| A | B |\r\n| - | - |\r\n| 1 | 2 |\r\n\r\n```md\r\n# not a heading\r\n```\r\n";

        var blocks = MarkdownDocumentBlockBuilder.Build(markdown);

        blocks.Select(block => block.Kind).Should().Equal(
            DocumentBlockKind.Text,
            DocumentBlockKind.Table,
            DocumentBlockKind.Code);
        foreach (var block in blocks)
        {
            block.SourceSpan.Should().NotBeNull();
            var span = block.SourceSpan!;
            markdown[span.Start..span.End].Should().Be(block.Content);
        }
    }
}
