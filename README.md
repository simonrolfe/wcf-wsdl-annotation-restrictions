# wcf-wsdl-annotation-restrictions

A way of using [ASP.NET MVC-style attributes](https://docs.microsoft.com/en-us/aspnet/mvc/overview/older-versions/mvc-music-store/mvc-music-store-part-6) to generate restrictions in WSDL files generated through WCF. Hopelessly outdated now, but possibly interesting nonetheless.

This was built due to an odd set of requirements: the supplier that I was writing a service for insisted that a very particular WSDL be followed, despite them being the *client* of the service, and the service was required to generate the WSDL file systematically rather than just "faking" a static file, to ensure that the WSDL couldn't get out of sync with the service.

WCF has fairly loose restrictions schema-based data validation, only checking that something is a valid type for input to the service, so, for example, any integer may be valid by the WSDL even though only 6-digit numbers are actually accepted by the service. It is usually up to the service to then validate that information and return SOAP errors accordingly.

It was these two seemingly conflicting requirements that led me to research how WSDL/XSD restrictions were generated 
by WCF, discover the extensibility points (which were barely documented), and try to plug in my own restrictions.

## How it works

WCF provides [an extension point](https://docs.microsoft.com/en-us/dotnet/api/system.servicemodel.description.iwsdlexportextension?view=netframework-4.7.2) to allow injection of extra information into a WSDL file. The methods are all extremely general, don't offer much obvious guidance, and weren't well documented at the time, so time was needed to understand [MessageDescriptions](https://docs.microsoft.com/en-us/dotnet/api/system.servicemodel.description.messagedescription?view=netframework-4.7.2), [OperationDescriptions](https://docs.microsoft.com/en-us/dotnet/api/system.servicemodel.description.operationdescription?view=netframework-4.7.2), and [MessagePartDescriptions](https://docs.microsoft.com/en-us/dotnet/api/system.servicemodel.description.messagepartdescription?view=netframework-4.7.2), and various types of [XmlSchemaFacet](https://docs.microsoft.com/en-gb/dotnet/api/system.xml.schema.xmlschemafacet?view=netframework-4.7.1). I did this largely through the Immediate window, breakpoints on lots of empty functions implementing extension interfaces, and generating huge numbers of WSDLs to see what the in-box options could do.

## Things that went well

### It helps web developers that aren't used to WCF

The team implementing the service had some experience of ASP.NET MVC but none in WCF, and, other than me, were flat out on the actual functionality of the service. I built this specifically with the rest of the team in mind, allowing them to think of WCF services in much the same way as ASP.NET MVC controllers, and using the same sorts of data annotations they had become used to. About 30% of the complexity (basically, all of the reflection) here was in allowing reuse of the [System.ComponentModel.DataAnnotations](https://docs.microsoft.com/en-gb/dotnet/api/system.componentmodel.dataannotations?view=netframework-4.7.1) attributes.

### It's moderately easy to extend

Although the structure isn't particularly amenable to extension, the code is fairly clear and it's easy to see what I would need to do to add new types of restriction from here.

### It will work with newer versions of ASP.NET MVC

In the back of my mind at the time was the team's transition from MVC 3 to 4, so I was especially careful to not do anything *too* undocumented or seemingly version-specific. Looking back, I appear to have achieved this reasonably well. 

## Things that I'd do differently now

### There are no tests

This is pretty much unforgivable for a project in this vein: it should be reasonably  simple (if possibly quite time-consuming) to build a set of decent tests that cover all of the major cases and confirm that attributes are being picked up, generating the correct entries in the WSDL, etc. At the time, I wasn't much good at building tests, and I hadn't made the shift to realising the benefit of building them even if they would have taken a while.

### It's not as extensible as I'd like

There is a nominal sort of structure to adding new restriction types here, but it requires editing a few different functions, and a richer (possibly abstract/override-based) setup would be substiantially neater. That said, this covered all the restriction types that were needed for the project, so it was good enough at the time.