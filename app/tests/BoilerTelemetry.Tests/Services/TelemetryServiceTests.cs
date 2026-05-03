using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Services;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;

namespace BoilerTelemetry.Tests.Services;

public class TelemetryServiceTests
{
    private readonly Mock<ITelemetryRepository> _repoMock = new();
    private readonly Mock<ITelemetryPublisher> _publisherMock = new();
    private readonly TelemetryService _sut;

    public TelemetryServiceTests()
    {
        _sut = new TelemetryService(_repoMock.Object, _publisherMock.Object);
    }

    [Fact]
    public async Task IngestAsync_CallsBothRepositoryAndPublisher()
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, 8.0, DateTime.UtcNow);

        await _sut.IngestAsync(dto, default);

        _repoMock.Verify(r => r.WriteAsync(It.IsAny<TelemetryReading>(), It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<TelemetryReading>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IngestAsync_PassesCorrectValuesToRepository()
    {
        var boilerId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        var dto = new TelemetryRequestDto(boilerId, 75.0, 8.0, timestamp);

        TelemetryReading? captured = null;
        _repoMock
            .Setup(r => r.WriteAsync(It.IsAny<TelemetryReading>(), It.IsAny<CancellationToken>()))
            .Callback<TelemetryReading, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        await _sut.IngestAsync(dto, default);

        captured.Should().NotBeNull();
        captured!.BoilerId.Should().Be(boilerId);
        captured.Temperature.Should().Be(75.0);
        captured.Pressure.Should().Be(8.0);
        captured.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public async Task IngestAsync_PassesSameReadingToPublisher()
    {
        var boilerId = Guid.NewGuid();
        var dto = new TelemetryRequestDto(boilerId, 95.0, 12.0, DateTime.UtcNow);

        TelemetryReading? published = null;
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<TelemetryReading>(), It.IsAny<CancellationToken>()))
            .Callback<TelemetryReading, CancellationToken>((r, _) => published = r)
            .Returns(Task.CompletedTask);

        await _sut.IngestAsync(dto, default);

        published.Should().NotBeNull();
        published!.BoilerId.Should().Be(boilerId);
        published.Temperature.Should().Be(95.0);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsMappedDtos()
    {
        var boilerId = Guid.NewGuid();
        var from = DateTime.UtcNow.AddHours(-2);
        var to = DateTime.UtcNow;
        var readings = new List<TelemetryReading>
        {
            new() { BoilerId = boilerId, Temperature = 70, Pressure = 7, Timestamp = from.AddMinutes(10) },
            new() { BoilerId = boilerId, Temperature = 80, Pressure = 9, Timestamp = from.AddMinutes(20) }
        };
        _repoMock.Setup(r => r.QueryAsync(boilerId, from, to, It.IsAny<CancellationToken>())).ReturnsAsync(readings);

        var result = await _sut.GetHistoryAsync(boilerId, from, to, default);

        result.Should().HaveCount(2);
        result[0].Temperature.Should().Be(70);
        result[0].Pressure.Should().Be(7);
        result[1].Temperature.Should().Be(80);
        result.Should().AllSatisfy(r => r.BoilerId.Should().Be(boilerId));
    }

    [Fact]
    public async Task GetHistoryAsync_WhenNoReadings_ReturnsEmptyList()
    {
        _repoMock
            .Setup(r => r.QueryAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TelemetryReading>());

        var result = await _sut.GetHistoryAsync(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_PassesCorrectParametersToRepository()
    {
        var boilerId = Guid.NewGuid();
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _repoMock
            .Setup(r => r.QueryAsync(boilerId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TelemetryReading>());

        await _sut.GetHistoryAsync(boilerId, from, to, default);

        _repoMock.Verify(r => r.QueryAsync(boilerId, from, to, It.IsAny<CancellationToken>()), Times.Once);
    }
}
