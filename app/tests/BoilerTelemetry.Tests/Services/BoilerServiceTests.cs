using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Services;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;

namespace BoilerTelemetry.Tests.Services;

public class BoilerServiceTests
{
    private readonly Mock<IBoilerRepository> _repositoryMock = new();
    private readonly BoilerService _sut;

    public BoilerServiceTests()
    {
        _sut = new BoilerService(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WhenBoilerExists_ReturnsDto()
    {
        var id = Guid.NewGuid();
        var boiler = new Boiler { Id = id, Name = "Test", Location = "Room", TemperatureThreshold = 80, PressureThreshold = 10 };
        _repositoryMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(boiler);

        var result = await _sut.GetByIdAsync(id, default);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Test");
        result.TemperatureThreshold.Should().Be(80);
    }

    [Fact]
    public async Task GetByIdAsync_WhenBoilerNotFound_ReturnsNull()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Boiler?)null);

        var result = await _sut.GetByIdAsync(Guid.NewGuid(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsMappedDtos()
    {
        var boilers = new List<Boiler>
        {
            new() { Id = Guid.NewGuid(), Name = "B1", Location = "L1", TemperatureThreshold = 80, PressureThreshold = 10 },
            new() { Id = Guid.NewGuid(), Name = "B2", Location = "L2", TemperatureThreshold = 90, PressureThreshold = 12 }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(boilers);

        var result = await _sut.GetAllAsync(default);

        result.Should().HaveCount(2);
        result.Select(r => r.Name).Should().BeEquivalentTo("B1", "B2");
    }

    [Fact]
    public async Task CreateAsync_WhenNameIsUnique_CreatesAndReturnsDto()
    {
        var dto = new CreateBoilerDto("Boiler-1", "Engine Room", 85, 10);
        _repositoryMock.Setup(r => r.ExistsByNameAsync(dto.Name, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<Boiler>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Boiler b, CancellationToken _) => b);

        var result = await _sut.CreateAsync(dto, default);

        result.Should().NotBeNull();
        result.Name.Should().Be("Boiler-1");
        result.Location.Should().Be("Engine Room");
        result.TemperatureThreshold.Should().Be(85);
        result.PressureThreshold.Should().Be(10);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WhenNameAlreadyExists_ThrowsInvalidOperationException()
    {
        var dto = new CreateBoilerDto("Existing", "Room", 80, 10);
        _repositoryMock.Setup(r => r.ExistsByNameAsync(dto.Name, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _sut.Invoking(s => s.CreateAsync(dto, default))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Existing*");
    }

    [Fact]
    public async Task CreateAsync_DoesNotCallRepositoryCreate_WhenNameExists()
    {
        var dto = new CreateBoilerDto("Duplicate", "Room", 80, 10);
        _repositoryMock.Setup(r => r.ExistsByNameAsync(dto.Name, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _sut.Invoking(s => s.CreateAsync(dto, default)).Should().ThrowAsync<InvalidOperationException>();

        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Boiler>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenBoilerExists_UpdatesOnlyProvidedFields()
    {
        var id = Guid.NewGuid();
        var boiler = new Boiler { Id = id, Name = "Old Name", Location = "Old Loc", TemperatureThreshold = 80, PressureThreshold = 10 };
        _repositoryMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(boiler);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Boiler>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Boiler b, CancellationToken _) => b);

        var dto = new UpdateBoilerDto("New Name", null, 90, null);
        var result = await _sut.UpdateAsync(id, dto, default);

        result.Should().NotBeNull();
        result!.Name.Should().Be("New Name");
        result.TemperatureThreshold.Should().Be(90);
        result.Location.Should().Be("Old Loc");   // не изменялся
        result.PressureThreshold.Should().Be(10); // не изменялся
    }

    [Fact]
    public async Task UpdateAsync_WhenBoilerNotFound_ReturnsNull()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Boiler?)null);

        var result = await _sut.UpdateAsync(Guid.NewGuid(), new UpdateBoilerDto(null, null, null, null), default);

        result.Should().BeNull();
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Boiler>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenBoilerExists_ReturnsTrueAndCallsDelete()
    {
        var id = Guid.NewGuid();
        var boiler = new Boiler { Id = id };
        _repositoryMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(boiler);
        _repositoryMock.Setup(r => r.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.DeleteAsync(id, default);

        result.Should().BeTrue();
        _repositoryMock.Verify(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenBoilerNotFound_ReturnsFalseAndDoesNotCallDelete()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Boiler?)null);

        var result = await _sut.DeleteAsync(Guid.NewGuid(), default);

        result.Should().BeFalse();
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
