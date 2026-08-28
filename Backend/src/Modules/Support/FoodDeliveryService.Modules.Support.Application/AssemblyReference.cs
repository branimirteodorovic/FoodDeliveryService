using System.Reflection;

namespace FoodDeliveryService.Modules.Support.Application;

public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
