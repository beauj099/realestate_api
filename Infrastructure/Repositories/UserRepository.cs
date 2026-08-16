using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class UserRepository : DapperRepository, IUserRepository
{
    public UserRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<User?> GetByUsernameAsync(string username) =>
        QuerySingleOrDefaultSqlAsync<User>(
            "SELECT Id, Username, PasswordHash, DisplayName, Role, IsActive, CreatedAt FROM Users WHERE Username = @Username AND IsActive = 1",
            new { Username = username });
}
