namespace Pos.Api;

public static class OpenApiDocumentFactory
{
    public static object Create()
    {
        return new
        {
            openapi = "3.0.3",
            info = new { title = "Universal Commerce OS API", version = "v1" },
            paths = new Dictionary<string, object>
            {
                ["/health"] = Method("Health check"),
                ["/health/ready"] = Method("Readiness check"),
                ["/api/v1/tenancy/bootstrap"] = Method("Read seeded tenant hierarchy"),
                ["/api/v1/categories"] = Method("List categories"),
                ["/api/v1/products"] = Method("List or create products"),
                ["/api/v1/tax-categories"] = Method("List tax categories"),
                ["/api/v1/customers"] = Method("List or create customers"),
                ["/api/v1/inventory/balances"] = Method("List materialized stock balances"),
                ["/api/v1/inventory/opening-stock"] = Method("Append opening stock movement"),
                ["/api/v1/sales/complete"] = Method("Complete a sale and reduce inventory"),
                ["/api/v1/sales"] = Method("List completed sales"),
                ["/api/v1/reports/today"] = Method("Read daily KPIs")
            }
        };
    }

    private static object Method(string summary)
    {
        return new
        {
            get = new
            {
                summary,
                responses = new Dictionary<string, object>
                {
                    ["200"] = new { description = "Success" }
                }
            }
        };
    }
}
