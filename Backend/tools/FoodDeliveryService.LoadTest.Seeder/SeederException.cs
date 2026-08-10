namespace FoodDeliveryService.LoadTest.Seeder;

// S3871 wants these public so callers outside the assembly can catch them; CA1515 wants an
// executable's types internal because there are no such callers. CA1515 is right here — this is a
// console tool, and the only handler is Main.
#pragma warning disable S3871 // Exception types should be public

/// <summary>
/// A seeding step failed in a way the operator has to know about. Carries the message that gets
/// printed; there is no stack trace worth showing for "the Gateway said 403".
/// </summary>
internal sealed class SeederException(string message) : Exception(message);

/// <summary>The command line was wrong. Prints the usage block and exits 2.</summary>
internal sealed class SeederUsageException(string message) : Exception(message);

#pragma warning restore S3871
