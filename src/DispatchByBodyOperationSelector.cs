//  Copyright (c) Microsoft Corporation.  All Rights Reserved.
using System;
using System.Collections.Generic;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.Xml;

namespace Wsdl.AnnotationRestrictions
{
	class DispatchByBodyElementOperationSelector : IDispatchOperationSelector
	{
        Dictionary<XmlQualifiedName, string> dispatchDictionary;

        public DispatchByBodyElementOperationSelector(Dictionary<XmlQualifiedName, string> dispatchDictionary)
        {
            this.dispatchDictionary = dispatchDictionary;            
        }

        #region IDispatchOperationSelector Members

        public string SelectOperation(ref System.ServiceModel.Channels.Message message)
        {

            MessageBuffer buffer = message.CreateBufferedCopy(Int32.MaxValue);
            message = buffer.CreateMessage();
            
            var copy = buffer.CreateMessage();

            XmlDictionaryReader bodyReader = copy.GetReaderAtBodyContents();            
            XmlQualifiedName lookupQName = new XmlQualifiedName(bodyReader.LocalName, bodyReader.NamespaceURI);
            
            if (dispatchDictionary.ContainsKey(lookupQName))
            {
                // Mark Soap Headers action etc as understood
                //int actionHeaderIdx = message.Headers.FindHeader("Action", "http://www.w3.org/2005/08/addressing");
                //int actionHeaderIdx2 = message.Headers.FindHeader("Action", "*");
                //message.Headers.UnderstoodHeaders.Add((MessageHeaderInfo)message.Headers[actionHeaderIdx]);
                //message.Headers.UnderstoodHeaders.Add((MessageHeaderInfo)message.Headers.FindHeader(Headers[0]);

                return dispatchDictionary[lookupQName];
            }
            else
            {
                return null;
            }
        }
        #endregion
    }
}
