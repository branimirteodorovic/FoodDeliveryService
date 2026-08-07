// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires the base test class to be public.", Scope = "type", Target = "~T:FoodDeliveryService.Modules.FraudDetection.IntegrationTests.Abstractions.BaseIntegrationTest")]
[assembly: SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires the collection fixture to be public.", Scope = "type", Target = "~T:FoodDeliveryService.Modules.FraudDetection.IntegrationTests.Abstractions.IntegrationTestWebAppFactory")]
