using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Controllers;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;

namespace RigMD.Tests.Api;

public sealed class SessionFeedbackControllerTests
{
    private sealed class Repository : ISessionFeedbackRepository
    {
        public int Saves;
        public bool Missing, Fail;
        public Task<IReadOnlyList<SessionFeedbackDto>?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Internal failure must not be exposed.");
            return Task.FromResult<IReadOnlyList<SessionFeedbackDto>?>(Missing ? null : Array.Empty<SessionFeedbackDto>());
        }
        public Task<SessionFeedbackDto?> SaveAsync(Guid id, SaveSessionFeedbackRequest request, CancellationToken cancellationToken = default)
        {
            Saves++;
            if (Fail) throw new IOException("Internal failure must not be exposed.");
            return Task.FromResult<SessionFeedbackDto?>(Missing ? null : new(Guid.NewGuid(), id, request.Outcome, request.Note ?? "", DateTimeOffset.UtcNow));
        }
    }
    private static SessionFeedbackController Controller(Repository repository) => new(repository, NullLogger<SessionFeedbackController>.Instance);

    [Theory]
    [InlineData("still_happening")]
    [InlineData("not_happening")]
    [InlineData("not_sure")]
    public async Task ValidFeedbackReturnsSeparateObservation(string outcome)
    {
        var repository = new Repository(); var id = Guid.NewGuid();
        var result = Assert.IsType<OkObjectResult>(await Controller(repository).Save(id, new() { Outcome = outcome }, default));
        var value = Assert.IsType<SessionFeedbackDto>(result.Value);
        Assert.Equal(id, value.SessionId); Assert.Equal(outcome, value.Outcome); Assert.Equal(1, repository.Saves);
    }

    [Theory]
    [InlineData("fixed", 0)]
    [InlineData("", 0)]
    [InlineData("not_sure", 501)]
    public async Task InvalidFeedbackNeverReachesRepository(string outcome, int noteLength)
    {
        var repository = new Repository(); var request = new SaveSessionFeedbackRequest { Outcome = outcome, Note = new string('x', noteLength) };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
        Assert.IsType<BadRequestObjectResult>(await Controller(repository).Save(Guid.NewGuid(), request, default));
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task MissingChecksAndStorageFailuresAreHandledWithoutFalseSuccess()
    {
        var repository = new Repository { Missing = true }; var controller = Controller(repository); var id = Guid.NewGuid();
        Assert.IsType<NotFoundObjectResult>(await controller.Get(id, default));
        Assert.IsType<NotFoundObjectResult>(await controller.Save(id, new() { Outcome = "not_sure" }, default));
        repository.Missing = false; repository.Fail = true;
        Assert.Equal(500, Assert.IsType<ObjectResult>(await controller.Get(id, default)).StatusCode);
        var failure = Assert.IsType<ObjectResult>(await controller.Save(id, new() { Outcome = "not_sure" }, default));
        Assert.Equal(500, failure.StatusCode);
        Assert.DoesNotContain("Internal failure", System.Text.Json.JsonSerializer.Serialize(failure.Value));
    }
}
