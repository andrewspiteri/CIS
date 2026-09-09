namespace Cis.Modules.Repository.Tests;

public sealed class ReferencePreparationTests
{
    [Fact]
    public void NestAndTypeOrm_ExtractContractsFieldsRelationshipsAndStatesWithoutRedisLookups()
    {
        using var repository = new Fixture();
        repository.Write("src/products.ts", Source);
        repository.Write("src/ignored.spec.ts", Source.Replace("products", "test-products"));
        var seeds = RepositoryReferenceSeeder.Seed(repository.Path, new RepositoryClassifier().Classify(repository.Path));
        var api = Assert.Single(seeds["api-dictionary"]);
        Assert.Equal("/v1/products", api[3]);
        Assert.Contains("CreateProductDto", api[8]);
        Assert.Contains("Promise<Product>", api[9]);
        Assert.Contains("AuthGuard", api[10]);
        Assert.Contains("products.create", api[10]);
        Assert.Contains("Create a term deposit product", api[22]);
        Assert.Contains("ProductsController.create", api[22]);
        Assert.DoesNotContain("REDIS", string.Join('\n', api));
        Assert.DoesNotContain(seeds["data-dictionary"], row => row[0] == "Fake");
        var minimum = Assert.Single(seeds["data-dictionary"], row => row[1] == "minimumDeposit");
        Assert.Equal("Product", minimum[0]);
        Assert.Contains("precision=18", minimum[4]);
        var optional = Assert.Single(seeds["data-dictionary"], row => row[1] == "maximumDeposit");
        Assert.Equal("no", optional[3]);
        Assert.Equal("ProductBank", Assert.Single(seeds["erd"])[2]);
        Assert.Equal("ManyToOne", Assert.Single(seeds["erd"])[3]);
        Assert.Equal(2, seeds["workflow-state-dictionary"].Count);
        Assert.Single(seeds["permissions-dictionary"]);
    }

    [Fact]
    public void WorkflowDeclarations_WithTheSameNameInDifferentFilesHaveSeparateIdentities()
    {
        using var repository = new Fixture();
        repository.Write("src/auth.ts", "export enum VerificationStatus { Pending, Verified }");
        repository.Write("src/email.ts", "export enum VerificationStatus { Pending, Verified }");
        var seeds = RepositoryReferenceSeeder.Seed(repository.Path, new RepositoryClassifier().Classify(repository.Path));
        Assert.Equal(4, seeds["workflow-state-dictionary"].Count);
        Assert.Equal(2, seeds["workflow-state-dictionary"].Select(row => row[0]).Distinct().Count());
    }

    [Fact]
    public void Preparation_RefreshesUntouchedSeedsIsIdempotentAndPreservesHumanRowsAndActiveFiles()
    {
        using var repository = new Fixture();
        var initializer = new RepositoryInitializer();
        Assert.Equal(0, initializer.Initialize(new(repository.Path, "docs/cis", false, true)).ExitCode);
        var dataPath = System.IO.Path.Combine(repository.Path, "docs/cis/references/data-dictionary.md");
        var before = File.ReadAllText(dataPath);
        repository.Write("src/products.ts", Source);
        var preview = initializer.PrepareObservedReferences(repository.Path, apply: false);
        Assert.False(preview.Applied);
        Assert.Equal(before, File.ReadAllText(dataPath));
        Assert.Contains(preview.Inventories, item => item.Kind == "data-dictionary" && item.DiscoveredRows == 2);
        var prepared = initializer.PrepareObservedReferences(repository.Path);
        Assert.Equal(0, prepared.ExitCode);
        Assert.True(prepared.Applied);
        Assert.Contains("minimumDeposit", File.ReadAllText(dataPath));
        Assert.False(initializer.PrepareObservedReferences(repository.Path).Applied);
        var human = File.ReadAllText(dataPath).Replace("precision=18", "precision=18; Human-reviewed business interpretation");
        File.WriteAllText(dataPath, human);
        repository.Write("src/products.ts", Source.Replace("maximumDeposit: number | null;", "maximumDeposit: number | null;\n @Column()\n customerNote: string;"));
        Assert.True(initializer.PrepareObservedReferences(repository.Path).Applied);
        Assert.Contains("Human-reviewed business interpretation", File.ReadAllText(dataPath));
        Assert.Contains("customerNote", File.ReadAllText(dataPath));
        var active = File.ReadAllText(dataPath).Replace("status: Draft", "status: Active");
        File.WriteAllText(dataPath, active);
        var preserved = initializer.PrepareObservedReferences(repository.Path);
        Assert.Equal(active, File.ReadAllText(dataPath));
        Assert.Contains(preserved.Inventories, item => item.Kind == "data-dictionary" && item.Action == "preserved");
        Assert.Contains(preserved.Warnings, item => item.Contains("data-dictionary.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Preparation_GeneratesErdAssetsRepairsMissingImagesAndPreservesReviewedDiagrams()
    {
        using var repository = new Fixture();
        var initializer = new RepositoryInitializer();
        Assert.Equal(0, initializer.Initialize(new(repository.Path, "docs/cis", false, true)).ExitCode);
        repository.Write("src/products.ts", Source);
        var path = System.IO.Path.Combine(repository.Path, "docs/cis/references/erd.md");
        var before = File.ReadAllText(path);
        var imageFolder = System.IO.Path.Combine(repository.Path, "docs/cis/references/erd-diagrams");
        Assert.Equal(0, initializer.PrepareObservedReferences(repository.Path, apply: false).ExitCode);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(Directory.Exists(imageFolder));
        Assert.Equal(0, initializer.PrepareObservedReferences(repository.Path).ExitCode);
        Assert.Contains("![Entity relationship diagram]", File.ReadAllText(path));
        var image = Assert.Single(Directory.GetFiles(imageFolder, "*.svg"));
        Assert.False(initializer.PrepareObservedReferences(repository.Path).Applied);
        File.Delete(image);
        Assert.True(initializer.PrepareObservedReferences(repository.Path).Applied);
        Assert.True(File.Exists(image));
        File.AppendAllText(path, "\nHuman-maintained interpretation.\n");
        repository.Write("src/products.ts", Source.Replace("productBank: ProductBank;", "productBank: ProductBank;\n @ManyToOne(() => Customer)\n customer: Customer;"));
        Assert.Equal(0, initializer.PrepareObservedReferences(repository.Path).ExitCode);
        Assert.Contains("Human-maintained interpretation.", File.ReadAllText(path));
        Assert.Contains("Customer", string.Join("\n", Directory.GetFiles(imageFolder).Select(File.ReadAllText)));
        var active = File.ReadAllText(path).Replace("status: Draft", "status: Active");
        File.WriteAllText(path, active);
        initializer.PrepareObservedReferences(repository.Path);
        Assert.Equal(active, File.ReadAllText(path));
    }

    [Fact]
    public void TypeScriptBehavior_ExtractsObservedContractsWithoutQuotedExamplesOrSecretValues()
    {
        using var repository = new Fixture();
        repository.Write("src/behavior.ts", """
            import { BadRequestException } from '@nestjs/common';
            import { OnEvent } from '@nestjs/event-emitter';
            import { Process } from '@nestjs/bull';
            export class ProductProjectionService {
              @OnEvent('deposit.created')
              handle() {}
              @Process('reconcile')
              reconcile() {
                if (amount < minimum) { throw new BadRequestException('Deposit below minimum'); }
                if (status === 'private-value') { throw new BadRequestException('Invalid status'); }
                if (enabled) { save(); throw new Error('Later failure'); }
                this.events.emit('deposit.created', { depositId: deposit.id, token: process.env.ACCESS_TOKEN });
                this.events.emit(dynamicName, payload);
              }
            }
            // emitter.emit('fake.comment'); throw new Error('Fake');
            const example = "emitter.emit('fake.string'); throw new Error('Fake')";
            const token = process.env.ACCESS_TOKEN || 'do-not-copy-this-value';
            """);
        var seeds = RepositoryReferenceSeeder.Seed(repository.Path, new RepositoryClassifier().Classify(repository.Path));
        var observedEvent = Assert.Single(seeds["event-dictionary"]);
        Assert.Equal("deposit.created", observedEvent[1]);
        Assert.Contains("handler at", observedEvent[4]);
        Assert.Contains("depositId", observedEvent[6]);
        Assert.Contains(seeds["command-dictionary"], row => row[1] == "reconcile");
        Assert.Contains("declared projection", Assert.Single(seeds["projection-dictionary"])[1]);
        Assert.Contains(seeds["problem-details-catalogue"], row => row[2] == "400" && row[4] == "Deposit below minimum");
        Assert.Equal(3, seeds["problem-details-catalogue"].Count);
        Assert.Equal(2, seeds["business-invariant-catalogue"].Count);
        Assert.Contains(seeds["business-invariant-catalogue"], row => row[1].Contains("amount < minimum"));
        Assert.Contains(seeds["business-invariant-catalogue"], row => row[1].Contains("status === [string literal]"));
        Assert.Contains(seeds["configuration-dictionary"], row => row[0] == "ACCESS_TOKEN" && row[5] == "yes");
        Assert.DoesNotContain("do-not-copy-this-value", string.Join('\n', seeds.Values.SelectMany(rows => rows).SelectMany(row => row)));
        Assert.DoesNotContain("private-value", string.Join('\n', seeds["business-invariant-catalogue"].SelectMany(row => row)));
    }

    [Fact]
    public void WorkspacePreparation_RollsUpOwnedCodeWithScopeAndPreservesReviewedArtifacts()
    {
        using var authority = new Fixture();
        using var first = new Fixture();
        using var second = new Fixture();
        using var dependency = new Fixture();
        File.Delete(System.IO.Path.Combine(authority.Path, "package.json"));
        var initializer = new RepositoryInitializer();
        var registry = new WorkspaceRegistry(new CisRepositoryContextResolver());
        Assert.Equal(0, new WorkspaceInitializer(initializer, registry).Initialize(new(authority.Path, "docs/cis", false, true, "bank", "deposits")).ExitCode);
        var importer = new RepositoryImporter(initializer, registry);
        Assert.Equal(0, importer.Import(new(authority.Path, "docs/cis", [first.Path, second.Path], false, true, "owned", "none")).ExitCode);
        Assert.Equal(0, importer.Import(new(authority.Path, "docs/cis", [dependency.Path], false, true, "dependency", "producer")).ExitCode);
        first.Write("src/products.ts", Source);
        second.Write("src/products.ts", Source);
        dependency.Write("src/private.ts", Source.Replace("products", "external-only"));
        authority.Write("docs/cis/specs/business-requirements.md", "# Human BRD\nKeep this narrative unchanged.\n");
        string Read(Fixture fixture, string path) => File.ReadAllText(System.IO.Path.Combine(fixture.Path, "docs/cis/" + path));
        var brd = Read(authority, "specs/business-requirements.md");
        var technical = Read(authority, "specs/technical-intent-spec.md");
        var dependencyBefore = Read(dependency, "references/api-dictionary.md");
        var authorityBefore = Read(authority, "references/api-dictionary.md");
        var preview = initializer.PrepareWorkspaceObservedReferences(authority.Path, false);
        Assert.Equal(3, preview.Count);
        Assert.All(preview, report => { Assert.Empty(report.Errors); Assert.False(report.Applied); });
        Assert.Equal(authorityBefore, Read(authority, "references/api-dictionary.md"));
        var prepared = initializer.PrepareWorkspaceObservedReferences(authority.Path);
        Assert.All(prepared, report => Assert.Empty(report.Errors));
        var api = Read(authority, "references/api-dictionary.md");
        Assert.Contains(" Repository |", api);
        Assert.Equal(2, api.Split('\n').Count(line => line.StartsWith('|') && line.Contains("/v1/products")));
        Assert.Contains(System.IO.Path.GetFileName(first.Path), api);
        Assert.Contains(System.IO.Path.GetFileName(second.Path), api);
        Assert.DoesNotContain("external-only", api);
        Assert.DoesNotContain("| TODO |", api);
        Assert.Equal(dependencyBefore, Read(dependency, "references/api-dictionary.md"));
        Assert.Equal(brd, Read(authority, "specs/business-requirements.md"));
        Assert.Equal(technical, Read(authority, "specs/technical-intent-spec.md"));
        Assert.All(initializer.PrepareWorkspaceObservedReferences(authority.Path), report => Assert.False(report.Applied));
        var reviewed = api.Replace("status: Draft", "status: Active");
        authority.Write("docs/cis/references/api-dictionary.md", reviewed);
        var preserved = initializer.PrepareWorkspaceObservedReferences(authority.Path);
        Assert.Equal(reviewed, Read(authority, "references/api-dictionary.md"));
        Assert.Contains(preserved.Last().Inventories, item => item.Kind == "api-dictionary" && item.Action == "preserved");
    }

    private const string Source = """
        import { Controller, Post, Body, UseGuards } from '@nestjs/common';
        import { Entity, Column, ManyToOne, JoinColumn } from 'typeorm';
        // @Get('fake')
        /* @Entity() class Fake { @Column() nope: number; } */
        @UseGuards(AuthGuard)
        @Controller('v1/products')
        export class ProductsController {
          @Post()
          @RequirePermissions('products.create')
          @AuditTrail({ snapshotExtractor: (req, res) => {
            if (res.data) { return res.data; }
            return res;
          } })
          @ApiOperation({ summary: 'Create a term deposit product' })
          async create(@Body() body: CreateProductDto): Promise<Product> {
            return service.create(body);
          }
        }
        @Entity({ name: 'products' })
        export class Product {
          @Column({ type: 'decimal', precision: 18, scale: 2 })
          minimumDeposit: number;
          @Column({ type: 'decimal', nullable: true, default: () => '(1 + 2)' })
          maximumDeposit: number | null;
          @ManyToOne(() => ProductBank)
          @JoinColumn({ name: 'product_bank_id' })
          productBank: ProductBank;
        }
        export enum DepositStatus { Pending = 'pending', Matured = 'matured' }
        // enum FakeStatus { Never }
        const documentation = "@RequirePermissions('fake.permission')";
        app.get('REDIS_CLIENT');
        app.get('REDIS_SUBSCRIBER');
        """;

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-reference-preparation-tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Write("package.json", """{"name":"deposits","dependencies":{"@nestjs/core":"1","typeorm":"1","@nestjs/common":"1"}}""");
        public void Write(string relative, string content)
        {
            var path = System.IO.Path.Combine(Path, relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        }
        public void Dispose()
        {
            var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-reference-preparation-tests")) + System.IO.Path.DirectorySeparatorChar;
            if (System.IO.Path.GetFullPath(Path).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(Path, true);
        }
    }
}
