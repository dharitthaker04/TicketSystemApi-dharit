using System;
using System.Linq;
using System.Net;
using System.Web.Http;
using System.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TicketSystemApi.Models;
using TicketSystemApi.Services;

namespace TicketSystemApi.Controllers
{
    [RoutePrefix("cases")]
    public class CaseController : ApiController
    {
        private readonly ICrmService _crmService;

        public CaseController()
        {
            _crmService = new CrmService();
        }

        [HttpPost]
        [Route("create")]
        public IHttpActionResult CreateCase([FromBody] CaseRequestModel model)
        {
            // ✅ Step 0: Validate Bearer Token
            var authHeader = Request.Headers.Authorization;
            string expectedToken = ConfigurationManager.AppSettings["AzureBearerToken"];

            if (authHeader == null || authHeader.Scheme != "Bearer" || authHeader.Parameter != expectedToken)
            {
                return Content(HttpStatusCode.Unauthorized,
                    ApiResponse<object>.Error("Unauthorized - Invalid bearer token"));
            }

            // ✅ Step 1: Validate input
            if (model == null || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Incident))
            {
                return Ok(ApiResponse<object>.Error("Missing required fields"));
            }

            try
            {
                var service = _crmService.GetService();

                // ✅ Step 2: Check for existing contact by email
                var contactQuery = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid", "firstname", "lastname"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("emailaddress1", ConditionOperator.Equal, model.Email)
                        }
                    }
                };

                var result = service.RetrieveMultiple(contactQuery);
                var contact = result.Entities.FirstOrDefault();
                Guid contactId;

                // ✅ Step 3: Create or update contact
                if (contact == null)
                {
                    var newContact = new Entity("contact");
                    newContact["firstname"] = model.FirstName;
                    newContact["lastname"] = model.LastName;
                    newContact["emailaddress1"] = model.Email;

                    contactId = service.Create(newContact);
                }
                else
                {
                    contact["firstname"] = model.FirstName;
                    contact["lastname"] = model.LastName;
                    service.Update(contact);

                    contactId = contact.Id;
                }

                // ✅ Step 4: Create Case (incident)
                var caseEntity = new Entity("incident");
                caseEntity["title"] = "Case created via Chatbot";
                caseEntity["description"] = model.Incident;
                caseEntity["customerid"] = new EntityReference("contact", contactId);
                caseEntity["new_ticketsubmissionchannel"] = new OptionSetValue(6);

                var caseId = service.Create(caseEntity);

                // ✅ Step 5: Retrieve ticket number
                var createdCase = service.Retrieve("incident", caseId, new ColumnSet("ticketnumber"));
                var ticketNumber = createdCase.Contains("ticketnumber") ? createdCase["ticketnumber"].ToString() : null;

                return Ok(ApiResponse<object>.Success(new
                {
                    CaseId = caseId,
                    TicketNumber = ticketNumber
                }, "Case created successfully"));
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.InternalServerError,
                    ApiResponse<object>.Error($"CRM error: {ex.Message}"));
            }
        }
    }
}
