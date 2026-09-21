using RealEstateApi.Application.DTOs;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

public class AgentProfileService
{
    private readonly UserRepository _userRepository;

    public AgentProfileService(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<AgentProfileDto?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return null;
        return new AgentProfileDto(
            user.Id, user.DisplayName, user.Email, user.Mobile,
            user.AgencyName, user.AgencyRegistrationNumber, user.LicenceNumber, user.Role);
    }

    public async Task<AgentProfileDto?> UpdateAsync(int userId, UpdateAgentProfileRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existing is not null && existing.Id != userId)
            throw new InvalidOperationException("Email address is already registered.");

        var updated = await _userRepository.UpdateProfileAsync(
            userId,
            request.DisplayName.Trim(),
            normalizedEmail,
            request.Mobile.Trim(),
            request.AgencyName?.Trim(),
            request.AgencyRegistrationNumber?.Trim(),
            request.LicenceNumber?.Trim(),
            cancellationToken);
        if (updated is null) return null;
        return new AgentProfileDto(
            updated.Id, updated.DisplayName, updated.Email, updated.Mobile,
            updated.AgencyName, updated.AgencyRegistrationNumber, updated.LicenceNumber, updated.Role);
    }
}
