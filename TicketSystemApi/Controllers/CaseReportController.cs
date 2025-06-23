using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TicketSystemApi.Services;

[RoutePrefix("api/cases")]
public class CaseReportController : ApiController
{
    [HttpGet]
    [Route("report")]
    public IHttpActionResult GetCases(string filter = "all", int page = 1, int pageSize = 10)
    {
       
        try
        {
            var expectedToken = System.Configuration.ConfigurationManager.AppSettings["ReportDataToken"];

            string token = "";

            if (Request.Headers.Authorization != null && Request.Headers.Authorization.Scheme == "Bearer")
            {
                token = Request.Headers.Authorization.Parameter;
            }
            else if (Request.GetQueryNameValuePairs().Any(kvp => kvp.Key == "token"))
            {
                token = Request.GetQueryNameValuePairs().First(kvp => kvp.Key == "token").Value;
            }

            if (token != expectedToken)
            {
                return Unauthorized();
            }

            ICrmService crm = new CrmService();
            var service = crm.GetService();

            var query = new QueryExpression("incident")
            {
                ColumnSet = new ColumnSet(
                    "ticketnumber",
                    "createdon",
                    "modifiedon",
                    "statuscode",
                    "prioritycode",
                    "new_ticketclosuredate",
                    "new_description",
                    "new_ticketsubmissionchannel",
                    "new_businessunitid",
                    "createdby",
                    "modifiedby",
                    "ownerid",
                    "customerid",
                    "new_tickettype",
                    "new_mainclassification",
                    "new_subclassificationitem"
                ),

                PageInfo = new PagingInfo
                {
                    PageNumber = page,
                    Count = pageSize,
                    PagingCookie = null
                }
            };

            if (filter.ToLower() == "daily")
                query.Criteria.AddCondition("createdon", ConditionOperator.Today);
            else if (filter.ToLower() == "weekly")
                query.Criteria.AddCondition("createdon", ConditionOperator.ThisWeek);
            else if (filter.ToLower() == "monthly")
                query.Criteria.AddCondition("createdon", ConditionOperator.ThisMonth);

            var result = service.RetrieveMultiple(query);

            var records = result.Entities.Select(e => new
            {
                TicketID = e.GetAttributeValue<string>("ticketnumber"),

                CreatedBy = e.GetAttributeValue<EntityReference>("createdby")?.Name,

                AgentName = e.GetAttributeValue<EntityReference>("ownerid")?.Name,

                UserID = e.GetAttributeValue<EntityReference>("customerid")?.Id,

                UserName = e.GetAttributeValue<EntityReference>("customerid")?.Name,


                CreatedOn = e.GetAttributeValue<DateTime?>("createdon")?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),

                TicketType = e.GetAttributeValue<EntityReference>("new_tickettype")?.Name,

                Category = e.GetAttributeValue<EntityReference>("new_tickettype")?.Name,  // alias for TicketType
                SubCategory1 = e.GetAttributeValue<EntityReference>("new_mainclassification")?.Name,
                SubCategory2 = e.GetAttributeValue<EntityReference>("new_subclassificationitem")?.Name,

                Status = e.FormattedValues.Contains("statuscode") ? e.FormattedValues["statuscode"] : null,

                TicketStatusDateTime = e.GetAttributeValue<DateTime?>("modifiedon")?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),

                Department = e.Attributes.Contains("new_businessunitid") ? ((EntityReference)e["new_businessunitid"]).Name : null,

                TicketChannel = e.FormattedValues.Contains("new_ticketsubmissionchannel") ? e.FormattedValues["new_ticketsubmissionchannel"] : null,

                TotalResolutionTime = (e.Contains("new_ticketclosuredate") && e.Contains("createdon"))
                    ? (e.GetAttributeValue<DateTime>("new_ticketclosuredate") - e.GetAttributeValue<DateTime>("createdon")).ToString(@"hh\:mm\:ss")
                    : null,

                TotalClosedTime = (e.Contains("modifiedon") && e.Contains("createdon"))
                    ? (e.GetAttributeValue<DateTime>("modifiedon") - e.GetAttributeValue<DateTime>("createdon")).ToString(@"hh\:mm\:ss")
                    : null,

                Description = e.GetAttributeValue<string>("new_description"),


                ModifiedBy = e.GetAttributeValue<EntityReference>("modifiedby")?.Name,


               

                Priority = e.FormattedValues.Contains("prioritycode") ? e.FormattedValues["prioritycode"] : null,

                ClosedOn = e.GetAttributeValue<DateTime?>("new_ticketclosuredate")?.ToLocalTime(),

                CustomerSatisfactionScore = GetCustomerSatisfactionScore(service, e.Id),


                SlaViolation = GetSlaViolationStatus(service, e.Id)

            }).ToList();

            return Ok(new
            {
                Page = page,
                PageSize = pageSize,
                Count = records.Count,
                Records = records
            });
        }
        catch (Exception ex)
        {
            return InternalServerError(ex);
        }
    }
    private string GetSlaViolationStatus(IOrganizationService service, Guid caseId)
    {
        var query = new QueryExpression("slakpiinstance")
        {
            ColumnSet = new ColumnSet("status"),
            Criteria = new FilterExpression
            {
                Conditions =
            {
                new ConditionExpression("regarding", ConditionOperator.Equal, caseId)
            }
            }
        };

        var kpiRecords = service.RetrieveMultiple(query);

        foreach (var kpi in kpiRecords.Entities)
        {
            // 3 = Noncompliant
            if (kpi.GetAttributeValue<OptionSetValue>("status")?.Value == 3)
                return "Yes";
        }

        return "No";
    }
    private int? GetCustomerSatisfactionScore(IOrganizationService service, Guid caseId)
    {
        var query = new QueryExpression("new_customersatisfactionscore")
        {
            ColumnSet = new ColumnSet("new_customersatisfactionrating"),
            Criteria = new FilterExpression
            {
                Conditions =
            {
                new ConditionExpression("new_csatcase", ConditionOperator.Equal, caseId)
            }
            }
        };

        var result = service.RetrieveMultiple(query);
        var record = result.Entities.FirstOrDefault();

        return record?.GetAttributeValue<OptionSetValue>("new_customersatisfactionrating")?.Value;
    }

}
