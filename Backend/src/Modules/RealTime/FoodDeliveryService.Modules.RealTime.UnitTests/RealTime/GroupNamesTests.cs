using AwesomeAssertions;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime;

public class GroupNamesTests
{
    [Fact]
    public void User_ShouldFormatWithUserPrefix()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string group = GroupNames.User(userId);

        group.Should().Be("user:11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Restaurant_ShouldFormatWithRestaurantPrefix()
    {
        var restaurantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        string group = GroupNames.Restaurant(restaurantId);

        group.Should().Be("restaurant:22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void Support_ShouldBeTheSingleGlobalGroup()
    {
        GroupNames.Support.Should().Be("support");
    }

    [Fact]
    public void User_ShouldBeDistinctPerUser()
    {
        string first = GroupNames.User(Guid.NewGuid());
        string second = GroupNames.User(Guid.NewGuid());

        first.Should().NotBe(second);
    }
}
