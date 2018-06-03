using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Runtime.Serialization;

namespace Sodexo.Web.Services
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class XSDGenerationAttribute : Attribute, IContractBehavior, IWsdlExportExtension
    {
        private Dictionary<string, XSDNamespace> _Namespaces;
        public string HeaderComment { get; set; }

        #region IContractBehavior Members (nothing to be done)

        void IContractBehavior.AddBindingParameters(ContractDescription contractDescription, ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
        }

        void IContractBehavior.ApplyClientBehavior(ContractDescription contractDescription, ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
        }

        void IContractBehavior.ApplyDispatchBehavior(ContractDescription contractDescription, ServiceEndpoint endpoint, DispatchRuntime dispatchRuntime)
        {
        }

        void IContractBehavior.Validate(ContractDescription contractDescription, ServiceEndpoint endpoint)
        {
        }

        #endregion

        #region IWsdlExportExtension Members
        /// <summary>
        /// When ExportContract is called to generate the necessary metadata, we inspect the service
        /// contract and build a list of parameters that we'll need to adjust the XSD for later.
        /// </summary>
        void IWsdlExportExtension.ExportContract(WsdlExporter exporter, WsdlContractConversionContext context)
        {
            _Namespaces = new Dictionary<string, XSDNamespace>();

            if (!string.IsNullOrWhiteSpace(HeaderComment))
            {
                context.WsdlPortType.Documentation = string.Empty;
                
                XmlDocument owner = context.WsdlPortType.DocumentationElement.OwnerDocument;
                XmlElement summaryElement = owner.CreateElement("summary");
                summaryElement.InnerText = HeaderComment;
                context.WsdlPortType.DocumentationElement.AppendChild(summaryElement);
            }

            foreach (var operation in context.Contract.Operations)
            {
                foreach (MessageDescription message in operation.Messages.Where(m => m.Direction == MessageDirection.Input))
                { 
                    var parameters = operation.SyncMethod.GetParameters();
                    Debug.Assert(parameters.Length == message.Body.Parts.Count);

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        foreach (PropertyInfo pi in parameters[i].ParameterType.GetProperties())
                        { 
                            ExtractAttributes(pi, message, operation, message.Body.Parts[0]);
                        }
                    }
                }
            }
        }

        void ExtractAttributes(PropertyInfo pi, MessageDescription Message, OperationDescription Operation, MessagePartDescription MessagePartDescription)
        {
            object[] xmlElementAttributes = pi.GetCustomAttributes(typeof(XmlElementAttribute), false);
            object[] stringLengthAttributes = pi.GetCustomAttributes(typeof(StringLengthAttribute), false);
            object[] regexAttributes = pi.GetCustomAttributes(typeof(RegularExpressionAttribute), false);
            object[] rangeAttributes = pi.GetCustomAttributes(typeof(RangeAttribute), false);
            object[] dataMemberAttributes = pi.GetCustomAttributes(typeof(DataMemberAttribute), false);

            string ElemName = pi.Name;
            //pick up XmlElement's ElementName if used to match later
            if (xmlElementAttributes.Length > 0)
            {
                XmlElementAttribute xmlElementAttrib = xmlElementAttributes[0] as XmlElementAttribute;
                if (!string.IsNullOrWhiteSpace(xmlElementAttrib.ElementName))
                {
                    ElemName = xmlElementAttrib.ElementName;
                }
            }
            if (!_Namespaces.ContainsKey(MessagePartDescription.Namespace))
            {
                _Namespaces.Add(MessagePartDescription.Namespace, new XSDNamespace());
            }

            XSDNamespace Namespace = _Namespaces[MessagePartDescription.Namespace];

            if (!Namespace.ContainsKey(Operation.Name))
            {
                Namespace.Add(Operation.Name, new XSDMessage());
            }

            XSDMessage XsdMessage = Namespace[Operation.Name];

            for (int i = 0; i < stringLengthAttributes.Length; i++)
            {
                StringLengthAttribute sla = stringLengthAttributes[i] as StringLengthAttribute;

                XsdMessage.Add(ElemName, new StringLengthMessagePart()
                {
                    MinLength = sla.MinimumLength,
                    MaxLength = sla.MaximumLength
                });
            }

            for (int i = 0; i < regexAttributes.Length; i++)
            {
                RegularExpressionAttribute rea = regexAttributes[i] as RegularExpressionAttribute;

                XsdMessage.Add(ElemName, new RegexMessagePart()
                {
                    Regex = rea.Pattern
                });
            }

            for (int i = 0; i < rangeAttributes.Length; i++)
            {
                RangeAttribute raa = rangeAttributes[i] as RangeAttribute;

                XsdMessage.Add(ElemName, new RangeMessagePart()
                {
                    Min = raa.Minimum,
                    Max = raa.Maximum
                });
            }
            for (int i = 0; i < dataMemberAttributes.Length; i++)
            {
                DataMemberAttribute dma = dataMemberAttributes[i] as DataMemberAttribute;

                XsdMessage.Add(ElemName, new RequiredMessagePart() 
                {
                    Required = dma.IsRequired 
                });
            }

                //rough seach for properties that are "custom" classes: there isn't a better way of doing it that I can find.
                if (pi.PropertyType.IsClass && (pi.PropertyType.BaseType != pi.PropertyType) && !pi.PropertyType.Namespace.StartsWith("System.") && !pi.PropertyType.Namespace.StartsWith("Microsoft.")) //hopefully filter out base objects
                {
                    //iterate nested properties, looking for more attributes
                    foreach (PropertyInfo piNested in pi.PropertyType.GetProperties())
                    {
                        ExtractAttributes(piNested, Message, Operation, MessagePartDescription);
                    }
                }
        }

        /// <summary>
        /// When ExportEndpoint is called, the XML schemas have been generated. Now we can manipulate to
        /// our heart's content.
        /// </summary>
        void IWsdlExportExtension.ExportEndpoint(WsdlExporter exporter, WsdlEndpointConversionContext context)
        {
            if (_Namespaces == null)
            {
                // If we have defined two endpoints implementing the same contract within the same service,
                // this method will be called twice. We only need to modify the schema once however.
                return;
            }

            foreach (KeyValuePair<string, XSDNamespace> Namespace in _Namespaces)
            { 
                var schemas = exporter.GeneratedXmlSchemas.Schemas(Namespace.Key);

                foreach (XmlSchema schema in schemas)
                {
                    foreach (KeyValuePair<string, XSDMessage> Message in Namespace.Value)
                    {
                        XmlSchemaElement message = (XmlSchemaElement)schema.Elements[new XmlQualifiedName(Message.Key, Namespace.Key)];
                        XmlSchemaComplexType complexType = message.ElementSchemaType as XmlSchemaComplexType;
                        Debug.Assert(complexType != null, "Expected input message to be complex type.");
                        XmlSchemaSequence sequence = complexType.Particle as XmlSchemaSequence;
                        Debug.Assert(sequence != null, "Expected a sequence.");
                    
                        foreach (XmlSchemaElement item in sequence.Items)
                        {
                            if (item.ElementSchemaType is XmlSchemaComplexType)
                            {
                                ParseComplexTypeForAttributes(item.ElementSchemaType as XmlSchemaComplexType, Message.Value);
                            }
                            else
                            {
                                if(Message.Value.ContainsKey(item.Name))
                                {
                                    XmlSchemaSimpleType simpleType = item.ElementSchemaType as XmlSchemaSimpleType;
                                    Debug.Assert(simpleType == null, "Cannot apply restriction to anything but simple types.");
                                    if(simpleType != null)
                                    {
                                        SetSchemaFacets(simpleType, Message.Value[item.Name]);
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // Throw away the temporary list we generated
            _Namespaces = null;
        }

        private void ParseComplexTypeForAttributes(XmlSchemaComplexType complexType, XSDMessage Message)
        {
            foreach (XmlSchemaAttribute a in complexType.Attributes)
            {
                XmlSchemaSimpleType st = a.AttributeSchemaType;
                if (st != null &&  Message.ContainsKey(a.Name))
                {
                    SetSchemaFacets(st, Message[a.Name]);
                }
            }

            XmlSchemaSequence sequence = complexType.Particle as XmlSchemaSequence;
            if (sequence != null)
            {
                foreach (XmlSchemaElement sequenceItem in sequence.Items)
                {
                    XmlSchemaComplexType sequenceItemComplexType = sequenceItem.ElementSchemaType as XmlSchemaComplexType;
                    if (sequenceItemComplexType != null)
                    {
                        ParseComplexTypeForAttributes(sequenceItemComplexType, Message);
                    }

                    XmlSchemaSimpleType sequenceItemSimpleType = sequenceItem.ElementSchemaType as XmlSchemaSimpleType;
                    if (sequenceItemSimpleType != null)
                    {
                        if (Message.ContainsKey(sequenceItem.Name))
                        {
                            XmlSchemaSimpleType newType = SetSchemaFacets(sequenceItemSimpleType, Message[sequenceItem.Name]);
                            
                            sequenceItem.SchemaType = newType;
                            sequenceItem.SchemaTypeName = new XmlQualifiedName();
                        }
                    }

                    SetMinOccurs(sequenceItem, Message[sequenceItem.Name]);
                }
            }
        }

        private void SetMinOccurs(XmlSchemaElement element, List<MessagePart> MessageParts)
        {
            foreach (MessagePart messagePart in MessageParts)
            {
                RequiredMessagePart requiredMessagePart = messagePart as RequiredMessagePart;
                if (requiredMessagePart != null)
                {
                    if (requiredMessagePart.Required) //only update minOccurs if marked as required, leave it alone otherwise
                    {
                        element.MinOccurs = 1;
                        element.MinOccursString = "1";
                        return; //break out, nothing more to do
                    }
                }
            }
        }

        private XmlSchemaSimpleType SetSchemaFacets(XmlSchemaSimpleType simpleType, List<MessagePart> MessageParts)
        {
            Debug.Assert(!(simpleType.Content is XmlSchemaSimpleTypeUnion), "Cannot apply restrictions to unions.");
            Debug.Assert(!(simpleType.Content is XmlSchemaSimpleTypeList), "Cannot apply restrictions to lists.");
            
            XmlSchemaSimpleTypeRestriction oldRestriction = simpleType.Content as XmlSchemaSimpleTypeRestriction;
            XmlSchemaSimpleTypeRestriction restriction = new XmlSchemaSimpleTypeRestriction();

            //preserve existing facets
            if (oldRestriction != null)
            {
                foreach (XmlSchemaObject restrictionFacet in oldRestriction.Facets)
                {
                    if (!(restrictionFacet is XmlSchemaMaxLengthFacet) && !(restrictionFacet is XmlSchemaMinLengthFacet))
                    {
                        restriction.Facets.Add(restrictionFacet);
                    }
                }
            }

            foreach (MessagePart messagePart in MessageParts)
            {
                StringLengthMessagePart stringLengthMessagePart = messagePart as StringLengthMessagePart;
                RegexMessagePart regexMessagePart = messagePart as RegexMessagePart;
                RangeMessagePart rangeMessagePart = messagePart as RangeMessagePart;
                
                if (stringLengthMessagePart != null)
                {
                    if (stringLengthMessagePart.MaxLength > 0)
                    {
                        XmlSchemaMaxLengthFacet maxLengthFacet = new XmlSchemaMaxLengthFacet();
                        maxLengthFacet.Value = stringLengthMessagePart.MaxLength.ToString();
                        restriction.Facets.Add(maxLengthFacet);
                    }
                    if (stringLengthMessagePart.MinLength > 0)
                    {
                        XmlSchemaMinLengthFacet minLengthFacet = new XmlSchemaMinLengthFacet();
                        minLengthFacet.Value = stringLengthMessagePart.MinLength.ToString();
                        restriction.Facets.Add(minLengthFacet);
                    }
                }

                if (regexMessagePart != null)
                {
                    XmlSchemaPatternFacet patternFacet = new XmlSchemaPatternFacet();
                    patternFacet.Value = regexMessagePart.Regex;
                    restriction.Facets.Add(patternFacet);
                }

                if (rangeMessagePart != null)
                {
                    XmlSchemaMinInclusiveFacet minInclusiveFacet = new XmlSchemaMinInclusiveFacet();
                    minInclusiveFacet.Value = rangeMessagePart.Min.ToString();
                    restriction.Facets.Add(minInclusiveFacet);

                    XmlSchemaMaxInclusiveFacet maxInclusiveFacet = new XmlSchemaMaxInclusiveFacet();
                    maxInclusiveFacet.Value = rangeMessagePart.Max.ToString();
                    restriction.Facets.Add(maxInclusiveFacet);
                }
            }

            restriction.BaseTypeName = simpleType.QualifiedName;

            XmlSchemaSimpleType newType = new XmlSchemaSimpleType();
            newType.Content = restriction;
            
            return newType;
        }

        #endregion

        #region Nested types

        private class XSDNamespace : Dictionary<string, XSDMessage>
        {
        }

        private class XSDMessage : Dictionary<string, List<MessagePart>>
        {
            public void Add(string Item, MessagePart Part)
            {
                if (!this.ContainsKey(Item))
                {
                    this.Add(Item, new List<MessagePart>());
                }
                this[Item].Add(Part);
            }
        }

        private class MessagePart
        {
        }

        private class RequiredMessagePart : MessagePart
        {
            public bool Required { get; set; }
        }

        private class StringLengthMessagePart : MessagePart
        {
            public int MinLength { get; set; }
            public int MaxLength { get; set; }
        }

        private class RegexMessagePart : MessagePart
        {
            public string Regex { get; set; }
        }

        private class RangeMessagePart : MessagePart
        {
            public object Min { get; set; }
            public object Max { get; set; }
        }

        #endregion
    }
}