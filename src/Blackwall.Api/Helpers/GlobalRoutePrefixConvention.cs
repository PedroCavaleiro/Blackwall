using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Blackwall.Api.Helpers;

/// <summary>
/// Creates a global route prefix for all controllers
/// </summary>
/// <param name="routePrefix">The route prefix to create e.g. api</param>
public class GlobalRoutePrefixConvention(string routePrefix) : IApplicationModelConvention {

    private readonly AttributeRouteModel _routePrefix = new(new RouteAttribute(routePrefix));

    public void Apply(ApplicationModel application) {

        foreach (var controller in application.Controllers) {

            foreach (var selector in controller.Selectors) {

                if (selector.AttributeRouteModel != null) {
                    selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(
                        _routePrefix,
                        selector.AttributeRouteModel);
                } else
                    selector.AttributeRouteModel = _routePrefix;
            }
        }
    }
}