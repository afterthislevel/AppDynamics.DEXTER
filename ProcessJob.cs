﻿using AppDynamics.Dexter.DataObjects;
using AppDynamics.Dexter.Extensions;
using CsvHelper;
using Newtonsoft.Json.Linq;
using NLog;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;
using OfficeOpenXml.Table;
using OfficeOpenXml.Table.PivotTable;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace AppDynamics.Dexter
{
    public class ProcessJob
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private static Logger loggerConsole = LogManager.GetLogger("AppDynamics.Dexter.Console");

        #region Constants for metric retrieval

        private const string METRIC_TIME_MS = "Time (ms)";

        // Constants for metric naming
        private const string METRIC_ART_FULLNAME = "Average Response Time (ms)";
        private const string METRIC_CPM_FULLNAME = "Calls per Minute";
        private const string METRIC_EPM_FULLNAME = "Errors per Minute";
        private const string METRIC_EXCPM_FULLNAME = "Exceptions per Minute";
        private const string METRIC_HTTPEPM_FULLNAME = "HTTP Error Codes per Minute";

        //Overall Application Performance|Calls per Minute
        private const string METRIC_PATH_APPLICATION = "Overall Application Performance|{0}";

        //Overall Application Performance|Web|Calls per Minute
        //Overall Application Performance|*|Calls per Minute
        private const string METRIC_PATH_TIER = "Overall Application Performance|{0}|{1}";

        //Overall Application Performance|Web|Individual Nodes|*|Calls per Minute
        //Overall Application Performance|*|Individual Nodes|*|Calls per Minute
        private const string METRIC_PATH_NODE = "Overall Application Performance|{0}|Individual Nodes|{1}|{2}";

        //Business Transaction Performance|Business Transactions|Web|AppHttpHandler ashx services|Calls per Minute
        //Business Transaction Performance|Business Transactions|*|AppHttpHandler ashx services|Calls per Minute
        private const string METRIC_PATH_BUSINESS_TRANSACTION = "Business Transaction Performance|Business Transactions|{0}|{1}|{2}";

        //Business Transaction Performance|Business Transactions|Web|AppHttpHandler ashx services|Individual Nodes|*|Calls per Minute
        //Business Transaction Performance|Business Transactions|*|AppHttpHandler ashx services|Individual Nodes|*|Calls per Minute
        // Not going to support that one

        //Backends|Discovered backend call - Azure ACS OAuth CloudSync-login.windows.net-443|Calls per Minute
        private const string METRIC_PATH_BACKEND = "Backends|Discovered backend call - {0}|{1}";

        //Overall Application Performance|Web|External Calls|Call-HTTP to Discovered backend call - Azure ACS OAuth CloudSync-login.windows.net-443|Calls per Minute
        //Overall Application Performance|Web|Individual Nodes|*|External Calls|Call-HTTP to Discovered backend call - Azure ACS OAuth CloudSync-login.windows.net-443|Calls per Minute
        //Overall Application Performance|*|Individual Nodes|*|External Calls|Call-HTTP to Discovered backend call - Azure ACS OAuth CloudSync-login.windows.net-443|Calls per Minute
        // Not going to support that one

        //Errors|Web|CrmException|Errors per Minute
        private const string METRIC_PATH_ERROR = "Errors|{0}|{1}|{2}";

        //Errors|Web|CrmException|Individual Nodes|*|Errors per Minute
        // Not going to support that one

        //Service Endpoints|Web|CrmAction.Execute|Calls per Minute
        private const string METRIC_PATH_SERVICE_ENDPOINT = "Service Endpoints|{0}|{1}|{2}";

        //Service End Points|Web|CrmAction.Execute|Individual Nodes|*|Calls per Minute
        //Service End Points|*|CrmAction.Execute|Individual Nodes|*|Calls per Minute
        // Not going to support that one

        #endregion

        #region Constants for the folder and file names of data extract

        // Parent Folder names
        private const string ENTITIES_FOLDER_NAME = "ENT";
        private const string CONFIGURATION_FOLDER_NAME = "CFG";
        private const string METRICS_FOLDER_NAME = "METR";
        private const string SNAPSHOTS_FOLDER_NAME = "SNAP";
        private const string SNAPSHOT_FOLDER_NAME = "{0}.{1:yyyyMMddHHmmss}";
        private const string EVENTS_FOLDER_NAME = "EVT";
        private const string REPORTS_FOLDER_NAME = "RPT";

        // More folder names for entity types
        private const string APPLICATION_TYPE_SHORT = "APP";
        private const string TIERS_TYPE_SHORT = "TIER";
        private const string NODES_TYPE_SHORT = "NODE";
        private const string BACKENDS_TYPE_SHORT = "BACK";
        private const string BUSINESS_TRANSACTIONS_TYPE_SHORT = "BT";
        private const string SERVICE_ENDPOINTS_TYPE_SHORT = "SEP";
        private const string ERRORS_TYPE_SHORT = "ERR";

        // Metric folder names
        private const string METRIC_ART_SHORTNAME = "ART";
        private const string METRIC_CPM_SHORTNAME = "CPM";
        private const string METRIC_EPM_SHORTNAME = "EPM";
        private const string METRIC_EXCPM_SHORTNAME = "EXCPM";
        private const string METRIC_HTTPEPM_SHORTNAME = "HTTPEPM";

        private static Dictionary<string, string> metricNameToShortMetricNameMapping = new Dictionary<string, string>()
        {
            {METRIC_ART_FULLNAME, METRIC_ART_SHORTNAME},
            {METRIC_CPM_FULLNAME, METRIC_CPM_SHORTNAME},
            {METRIC_EPM_FULLNAME, METRIC_EPM_SHORTNAME},
            {METRIC_EXCPM_FULLNAME, METRIC_EXCPM_SHORTNAME},
            {METRIC_HTTPEPM_FULLNAME, METRIC_HTTPEPM_SHORTNAME},
        };

        // Metadata file names
        private const string EXTRACT_CONFIGURATION_APPLICATION_FILE_NAME = "configuration.xml";
        private const string EXTRACT_CONFIGURATION_CONTROLLER_FILE_NAME = "settings.json";
        private const string EXTRACT_ENTITY_APPLICATIONS_FILE_NAME = "applications.json";
        private const string EXTRACT_ENTITY_APPLICATION_FILE_NAME = "application.json";
        private const string EXTRACT_ENTITY_TIERS_FILE_NAME = "tiers.json";
        private const string EXTRACT_ENTITY_NODES_FILE_NAME = "nodes.json";
        private const string EXTRACT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME = "businesstransactions.json";
        private const string EXTRACT_ENTITY_BACKENDS_FILE_NAME = "backends.json";
        private const string EXTRACT_ENTITY_BACKENDS_DETAIL_FILE_NAME = "backendsdetail.json";
        private const string EXTRACT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME = "serviceendpoints.json";
        private const string EXTRACT_ENTITY_SERVICE_ENDPOINTS_DETAIL_FILE_NAME = "serviceendpointsdetail.json";
        private const string EXTRACT_ENTITY_ERRORS_FILE_NAME = "errors.json";
        private const string EXTRACT_ENTITY_NAME_FILE_NAME = "name.json";

        // Metric data file names
        private const string EXTRACT_METRIC_FULL_FILE_NAME = "full.{0:yyyyMMddHHmm}-{1:yyyyMMddHHmm}.json";
        private const string EXTRACT_METRIC_HOUR_FILE_NAME = "hour.{0:yyyyMMddHHmm}-{1:yyyyMMddHHmm}.json";

        // Flowmap file names
        private const string EXTRACT_ENTITY_FLOWMAP_FILE_NAME = "flowmap.{0:yyyyMMddHHmm}-{1:yyyyMMddHHmm}.json";

        // List of Snapshots file names
        private const string EXTRACT_SNAPSHOTS_FILE_NAME = "snapshots.{0:yyyyMMddHHmm}-{1:yyyyMMddHHmm}.json";
        private const int SNAPSHOTS_QUERY_PAGE_SIZE = 600;

        // Snapshot file names
        private const string EXTRACT_SNAPSHOT_FLOWMAP_FILE_NAME = "flowmap.json";
        private const string EXTRACT_SNAPSHOT_SEGMENT_FILE_NAME = "segments.json";
        private const string EXTRACT_SNAPSHOT_SEGMENT_DATA_FILE_NAME = "segment.{0}.json";
        private const string EXTRACT_SNAPSHOT_SEGMENT_CALLGRAPH_FILE_NAME = "callgraph.{0}.json";
        private const string EXTRACT_SNAPSHOT_SEGMENT_ERROR_FILE_NAME = "error.{0}.json";

        private const string SNAPSHOT_UX_NORMAL = "NORMAL";
        private const string SNAPSHOT_UX_SLOW = "SLOW";
        private const string SNAPSHOT_UX_VERY_SLOW = "VERY_SLOW";
        private const string SNAPSHOT_UX_STALL = "STALL";
        private const string SNAPSHOT_UX_ERROR = "ERROR";

        // Mapping for snapshot folder names
        private static Dictionary<string, string> userExperienceFolderNameMapping = new Dictionary<string, string>
        {
            {SNAPSHOT_UX_NORMAL, "NM"},
            {SNAPSHOT_UX_SLOW, "SL"},
            {SNAPSHOT_UX_VERY_SLOW, "VS"},
            {SNAPSHOT_UX_STALL, "ST"},
            {SNAPSHOT_UX_ERROR, "ER"}
        };

        // Snapshots file names
        private const string HEALTH_RULE_VIOLATIONS_FILE_NAME = "healthruleviolations.{0:yyyyMMddHHmm}-{1:yyyyMMddHHmm}.json";
        private const string EVENTS_FILE_NAME = "{0}.{1:yyyyMMddHHmm}-{2:yyyyMMddHHmm}.json";

        // There are a bazillion types of events
        // Source C:\appdynamics\codebase\controller\controller-api\agent\src\main\java\com\singularity\ee\controller\api\constants\EventType.java
        // They are sort of documented here:
        //      https://docs.appdynamics.com/display/PRO43/Remediation+Scripts
        //      https://docs.appdynamics.com/display/PRO43/Build+a+Custom+Action
        // Choosing select few that I care about
        private static List<string> eventTypes = new List<string>
        {
            // Events UI: Application Changes
            // App Server Restart
            { "APP_SERVER_RESTART" },
            // Thrown when application parameters change, like JVM options, etc
            { "APPLICATION_CONFIG_CHANGE" },
            // This is injected by user / REST API.
            { "APPLICATION_DEPLOYMENT" },

            // Events UI: Code problems
            // Code deadlock detected by Agent
            { "DEADLOCK" },
            // This is thrown when any resource pool size is reached, thread pool, connection pool etc. fall into this category
            { "RESOURCE_POOL_LIMIT" },
                       
            // Events UI: Custom
            // Custom Events thrown by API calls using REST or machine agent API
            { "CUSTOM" },

            // Events UI: Server Crashes
            { "APPLICATION_CRASH" },
            // CLR Crash
            { "CLR_CRASH" },            

            // Events UI: Health Rule Violations
            // Health rules
            { "POLICY_OPEN_WARNING" },
            { "POLICY_OPEN_CRITICAL" },
            { "POLICY_CLOSE_WARNING" },
            { "POLICY_CLOSE_CRITICAL" },
            { "POLICY_UPGRADED" },
            { "POLICY_DOWNGRADED" },
            { "POLICY_CANCELED_WARNING" },
            { "POLICY_CANCELED_CRITICAL" },
            { "POLICY_CONTINUES_CRITICAL" },
            { "POLICY_CONTINUES_WARNING" },

            // Events UI: Error
            // This is thrown when the agent detects and error NOT during a BT (no BT id on thread)
            { "APPLICATION_ERROR" },

            // Events UI: Not possible - this is just a query here
            // Diagnostic session.  There are several subTypes for this.
            { "DIAGNOSTIC_SESSION" },
        };

        private const string ENTITY_TYPE_APPLICATION = "APPLICATION";
        private const string ENTITY_TYPE_APPLICATION_MOBILE = "MOBILE_APPLICATION";
        private const string ENTITY_TYPE_TIER = "APPLICATION_COMPONENT";
        private const string ENTITY_TYPE_NODE = "APPLICATION_COMPONENT_NODE";
        private const string ENTITY_TYPE_MACHINE = "MACHINE_INSTANCE";
        private const string ENTITY_TYPE_BUSINESS_TRANSACTION = "BUSINESS_TRANSACTION";
        private const string ENTITY_TYPE_BACKEND = "BACKEND";
        private const string ENTITY_TYPE_HEALTH_RULE = "POLICY";

        // Mapping of long entity types to human readable ones
        private static Dictionary<string, string> entityTypeStringMapping = new Dictionary<string, string>
        {
            {ENTITY_TYPE_APPLICATION, "Application"},
            {ENTITY_TYPE_APPLICATION_MOBILE, "Mobile App"},
            {ENTITY_TYPE_TIER, "Tier"},
            {ENTITY_TYPE_NODE, "Node"},
            {ENTITY_TYPE_MACHINE, "Machine"},
            {ENTITY_TYPE_BUSINESS_TRANSACTION, "BT"},
            {ENTITY_TYPE_BACKEND, "Backend" },
            {ENTITY_TYPE_HEALTH_RULE, "Health Rule"}
        };

        #endregion

        #region Constants for the folder and file names of data index

        // Detected entity report conversion file names
        private const string CONVERT_ENTITY_CONTROLLER_FILE_NAME = "controller.csv";
        private const string CONVERT_ENTITY_CONTROLLERS_FILE_NAME = "controllers.csv";
        private const string CONVERT_ENTITY_APPLICATIONS_FILE_NAME = "applications.csv";
        private const string CONVERT_ENTITY_APPLICATION_FILE_NAME = "application.csv";
        private const string CONVERT_ENTITY_TIERS_FILE_NAME = "tiers.csv";
        private const string CONVERT_ENTITY_NODES_FILE_NAME = "nodes.csv";
        private const string CONVERT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME = "businesstransactions.csv";
        private const string CONVERT_ENTITY_BACKENDS_FILE_NAME = "backends.csv";
        private const string CONVERT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME = "serviceendpoints.csv";
        private const string CONVERT_ENTITY_ERRORS_FILE_NAME = "errors.csv";

        // Metric report conversion file name
        private const string CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME = "entities.full.csv";
        private const string CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME = "entities.hour.csv";
        private const string CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME = "entity.full.csv";
        private const string CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME = "entity.hour.csv";
        private const string CONVERT_METRIC_VALUES_FILE_NAME = "metric.values.csv";
        private const string CONVERT_METRIC_SUMMARY_FILE_NAME = "metric.summary.csv";

        // Flow map to flow grid conversion file name
        private const string CONVERT_ACTIVITY_GRID_FILE_NAME = "activitygrid.full.csv";

        // Events list
        private const string CONVERT_EVENTS_FILE_NAME = "events.csv";
        private const string CONVERT_EVENTS_FILTERED_FILE_NAME = "events.{0}.csv";
        private const string CONVERT_HEALTH_RULE_EVENTS_FILTERED_FILE_NAME = "hrviolationevents.{0}.csv";
        private const string CONVERT_HEALTH_RULE_EVENTS_FILE_NAME = "hrviolationevents.csv";

        // Snapshot list
        private const string CONVERT_SNAPSHOTS_FILE_NAME = "snapshots.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_FILE_NAME = "snapshots.segments.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_EXIT_CALLS_FILE_NAME = "snapshots.exits.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_SERVICE_ENDPOINTS_CALLS_FILE_NAME = "snapshots.serviceendpoints.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_DETECTED_ERRORS_FILE_NAME = "snapshots.errors.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_BUSINESS_DATA_FILE_NAME = "snapshots.businessdata.csv";

        private const string CONVERT_SNAPSHOT_FILE_NAME = "snapshot.csv";
        private const string CONVERT_SNAPSHOT_SEGMENTS_FILE_NAME = "snapshot.segments.csv";
        private const string CONVERT_SNAPSHOT_SEGMENTS_EXIT_CALLS_FILE_NAME = "snapshot.exits.csv";
        private const string CONVERT_SNAPSHOT_SEGMENTS_SERVICE_ENDPOINT_CALLS_FILE_NAME = "snapshot.serviceendpoints.csv";
        private const string CONVERT_SNAPSHOT_SEGMENTS_DETECTED_ERRORS_FILE_NAME = "snapshot.errors.csv";
        private const string CONVERT_SNAPSHOT_SEGMENTS_BUSINESS_DATA_FILE_NAME = "snapshot.businessdata.csv";

        private const string CONVERT_SNAPSHOTS_FILTERED_FILE_NAME = "snapshots.{0}.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_FILTERED_FILE_NAME = "snapshots.segments.{0}.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_EXIT_CALLS_FILTERED_FILE_NAME = "snapshots.exits.{0}.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_SERVICE_ENDPOINT_CALLS_FILTERED_FILE_NAME = "snapshots.serviceendpoints.{0}.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_DETECTED_ERRORS_FILTERED_FILE_NAME = "snapshots.errors.{0}.csv";
        private const string CONVERT_SNAPSHOTS_SEGMENTS_BUSINESS_DATA_FILTERED_FILE_NAME = "snapshots.businessdata.{0}.csv";

        //private const string CONVERT_SNAPSHOT_FLOWMAP_FILE_NAME = "flowmap.json";
        //private const string CONVERT_SNAPSHOT_SEGMENT_ERRORS_FILE_NAME = "snapshot.errors.csv";
        //private const string CONVERT_SNAPSHOT_SEGMENT_DATA_COLLECTORS_FILE_NAME = "snapshot.datacollectors.csv";
        //private const string CONVERT_SNAPSHOT_SEGMENT_PROPERTIES_FILE_NAME = "snapshot.properties.csv";

        #endregion

        #region Constants for the folder and file names of data reports

        // Report file names
        private const string REPORT_DETECTED_ENTITIES_FILE_NAME = "{0}.{1:yyyyMMddHH}-{2:yyyyMMddHH}.DetectedEntities.xlsx";
        private const string REPORT_METRICS_ALL_ENTITIES_FILE_NAME = "{0}.{1:yyyyMMddHH}-{2:yyyyMMddHH}.EntityMetrics.xlsx";
        private const string REPORT_DETECTED_EVENTS_FILE_NAME = "{0}.{1:yyyyMMddHH}-{2:yyyyMMddHH}.Events.xlsx";
        private const string REPORT_SNAPSHOTS_FILE_NAME = "{0}.{1:yyyyMMddHH}-{2:yyyyMMddHH}.Snapshots.xlsx";

        // Per entity report names
        private const string REPORT_ENTITY_DETAILS_APPLICATION_FILE_NAME = "{0}.{1:yyyyMMddHH}-{2:yyyyMMddHH}.{3}.{4}.xlsx";
        private const string REPORT_ENTITY_DETAILS_ENTITY_FILE_NAME = "{0}.{1:yyyyMMddHH}-{2:yyyyMMddHH}.{3}.{4}.{5}.xlsx";
        private const string REPORT_SNAPSHOT_DETAILS_FILE_NAME = "{0}.{1:yyyyMMddHH}-{2:yyyyMMddHH}.{3}.{4}.{5}.{6}.{7:yyyyMMddHHmmss}.{8}.xlsx";

        #endregion

        #region Constants for Common Reports sheets

        private const string REPORT_SHEET_PARAMETERS = "1.Parameters";
        private const string REPORT_SHEET_TOC = "2.TOC";

        #endregion

        #region Constants for Detected Entities Report contents

        private const string REPORT_DETECTED_ENTITIES_SHEET_CONTROLLERS_LIST = "3.Controllers";
        private const string REPORT_DETECTED_ENTITIES_SHEET_APPLICATIONS_LIST = "4.Applications";
        private const string REPORT_DETECTED_ENTITIES_SHEET_TIERS_LIST = "5.Tiers";
        private const string REPORT_DETECTED_ENTITIES_SHEET_TIERS_PIVOT = "5.Tiers.Pivot";
        private const string REPORT_DETECTED_ENTITIES_SHEET_NODES_LIST = "6.Nodes";
        private const string REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_APPAGENT_PIVOT = "6.Nodes.Type.AppAgent";
        private const string REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_MACHINEAGENT_PIVOT = "6.Nodes.Type.MachineAgent";
        private const string REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LIST = "7.Backends";
        private const string REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_TYPE_PIVOT = "7.Backends.Type";
        private const string REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LOCATION_PIVOT = "7.Backends.Location";
        private const string REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LIST = "8.BTs";
        private const string REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_TYPE_PIVOT = "8.BTs.Type";
        private const string REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LOCATION_PIVOT = "8.BTs.Location";
        private const string REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LIST = "9.SEPs";
        private const string REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_TYPE_PIVOT = "9.SEPs.Type";
        private const string REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LOCATION_PIVOT = "9.SEPs.Location";
        private const string REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LIST = "10.Errors";
        private const string REPORT_DETECTED_ENTITIES_SHEET_ERRORS_TYPE_PIVOT = "10.Errors.Type";
        private const string REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LOCATION_PIVOT_LOCATION = "10.Errors.Location";

        private const string REPORT_DETECTED_ENTITIES_TABLE_TOC = "t_TOC";
        private const string REPORT_DETECTED_ENTITIES_TABLE_PARAMETERS_TARGETS = "t_InputTargets";
        private const string REPORT_DETECTED_ENTITIES_TABLE_CONTROLLERS = "t_Controllers";
        private const string REPORT_DETECTED_ENTITIES_TABLE_APPLICATIONS = "t_Applications";
        private const string REPORT_DETECTED_ENTITIES_TABLE_TIERS = "t_Tiers";
        private const string REPORT_DETECTED_ENTITIES_TABLE_NODES = "t_Nodes";
        private const string REPORT_DETECTED_ENTITIES_TABLE_BACKENDS = "t_Backends";
        private const string REPORT_DETECTED_ENTITIES_TABLE_BUSINESS_TRANSACTIONS = "t_BusinessTransactions";
        private const string REPORT_DETECTED_ENTITIES_TABLE_SERVICE_ENDPOINTS = "t_ServiceEndpoints";
        private const string REPORT_DETECTED_ENTITIES_TABLE_ERRORS = "t_Errors";

        private const string REPORT_DETECTED_ENTITIES_PIVOT_TIERS = "p_Tiers";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_NODES_TYPE_APPAGENT = "p_NodesTypeAppAgent";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_NODES_TYPE_MACHINEAGENT = "p_NodesTypeMachineAgent";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_BACKENDS_TYPE = "p_BackendsType";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_BACKENDS_LOCATION = "p_BackendsLocation";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_BUSINESS_TRANSACTIONS_TYPE = "p_BusinessTransactionsType";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_BUSINESS_TRANSACTIONS_LOCATION_SHEET = "p_BusinessTransactionsLocation";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_SERVICE_ENDPOINTS_TYPE = "p_ServiceEndpointsType";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_SERVICE_ENDPOINTS_LOCATION = "p_ServiceEndpointsLocation";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_ERRORS_TYPE = "p_ErrorsType";
        private const string REPORT_DETECTED_ENTITIES_PIVOT_ERRORS_LOCATION = "p_ErrorsLocation";

        private const int REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT = 4;
        private const int REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT = 6;

        #endregion

        #region Constants for All Entities Metrics Report contents

        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_CONTROLLERS_LIST = "3.Controllers";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_FULL = "4.Applications";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_HOURLY = "4.Applications.Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_FULL = "5.Tiers.Full";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_HOURLY = "5.Tiers.Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_FULL = "6.Nodes";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_HOURLY = "6.Nodes.Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_FULL = "7.Backends";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_HOURLY = "7.Backends.Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_FULL = "8.BTs";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_HOURLY = "8.BTs.Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_FULL = "9.SEPs";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_HOURLY = "9.SEPs.Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_FULL = "10.Errors";
        private const string REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_HOURLY = "10.Errors.Hourly";

        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_TOC = "t_TOC";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_PARAMETERS_TARGETS = "t_InputTargets";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_CONTROLLERS = "t_Controllers";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_APPLICATIONS_FULL = "t_Applications_Full";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_APPLICATIONS_HOURLY = "t_Applications_Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_TIERS_FULL = "t_Tiers_Full";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_TIERS_HOURLY = "t_Tiers_Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_NODES_FULL = "t_Nodes_Full";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_NODES_HOURLY = "t_Nodes_Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_BACKENDS_FULL = "t_Backends_Full";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_BACKENDS_HOURLY = "t_Backends_Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_BUSINESS_TRANSACTIONS_FULL = "t_BusinessTransactions_Full";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_BUSINESS_TRANSACTIONS_HOURLY = "t_BusinessTransactions_Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_SERVICE_ENDPOINTS_FULL = "t_ServiceEndpoints_Full";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_SERVICE_ENDPOINTS_HOURLY = "t_ServiceEndpoints_Hourly";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_ERRORS_FULL = "t_Errors_Full";
        private const string REPORT_METRICS_ALL_ENTITIES_TABLE_ERRORS_HOURLY = "t_Errors_Hourly";

        private const int REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT = 4;

        #endregion

        #region Constants for Detected Events and Health Rule Violations Report contents

        private const string REPORT_DETECTED_EVENTS_SHEET_CONTROLLERS_LIST = "3.Controllers";

        private const string REPORT_DETECTED_EVENTS_SHEET_EVENTS = "4.Events";
        private const string REPORT_DETECTED_EVENTS_SHEET_EVENTS_PIVOT = "4.Events.Type";

        private const string REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR = "5.HR Violations";
        private const string REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR_PIVOT = "5.HR Violations.Type";

        private const string REPORT_DETECTED_EVENTS_TABLE_CONTROLLERS = "t_Controllers";

        private const string REPORT_DETECTED_EVENTS_TABLE_EVENTS = "t_Events";
        private const string REPORT_DETECTED_EVENTS_TABLE_HEALTH_RULE_VIOLATION_EVENTS = "t_HealthRuleViolationEvents";

        private const string REPORT_DETECTED_EVENTS_PIVOT_EVENTS_TYPE = "p_EventsType";
        private const string REPORT_DETECTED_EVENTS_PIVOT_HEALTH_RULE_VIOLATION_EVENTS_TYPE = "p_HealthRuleViolationEventsType";

        private const int REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT = 4;
        private const int REPORT_DETECTED_EVENTS_PIVOT_SHEET_START_PIVOT_AT = 6;

        #endregion

        #region Constants for Entity Metric Details Report contents

        private const string REPORT_ENTITY_DETAILS_SHEET_CONTROLLERS_LIST = "3.Controllers";

        // Metric summaries full, hourly
        private const string REPORT_ENTITY_DETAILS_SHEET_SUMMARY = "4.Summary";
        // Raw metric data
        private const string REPORT_ENTITY_DETAILS_SHEET_METRICS = "6.Metric Detail";
        // Flowmap in grid view
        private const string REPORT_ENTITY_DETAILS_SHEET_ACTIVITYGRID = "7.Activity Flow";
        // Event data 
        private const string REPORT_ENTITY_DETAILS_SHEET_EVENTS = "8.Events";
        private const string REPORT_ENTITY_DETAILS_SHEET_EVENTS_PIVOT = "8.Events.Type";
        // Health rule 
        private const string REPORT_ENTITY_DETAILS_SHEET_EVENTS_HR = "9.HR Violations";
        private const string REPORT_ENTITY_DETAILS_SHEET_EVENTS_HR_PIVOT = "9.HR Violations.Type";
        // Snapshots and segments
        private const string REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS = "10.Snapshots";
        private const string REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS_PIVOT = "10.Snapshots.Type";
        private const string REPORT_ENTITY_DETAILS_SHEET_SEGMENTS = "11.Segments";
        private const string REPORT_ENTITY_DETAILS_SHEET_SEGMENTS_PIVOT = "11.Segments.Type";
        private const string REPORT_ENTITY_DETAILS_SHEET_EXIT_CALLS = "12.Exit Calls";
        private const string REPORT_ENTITY_DETAILS_SHEET_EXIT_CALLS_PIVOT = "12.Exit Calls.Type";
        private const string REPORT_ENTITY_DETAILS_SHEET_SERVICE_ENDPOINT_CALLS = "12.Service Endpoint Calls";
        private const string REPORT_ENTITY_DETAILS_SHEET_DETECTED_ERRORS = "13.Errors";
        private const string REPORT_ENTITY_DETAILS_SHEET_DETECTED_ERRORS_PIVOT = "13.Errors.Type";
        // Graphs, snapshots and events all lined up in timeline
        private const string REPORT_ENTITY_DETAILS_SHEET_HOURLY_TIMELINE = "5.Timeline";

        private const string REPORT_ENTITY_DETAILS_TABLE_TOC = "t_TOC";
        private const string REPORT_ENTITY_DETAILS_TABLE_CONTROLLERS = "t_Controllers";

        // Full and hourly metric data
        private const string REPORT_ENTITY_DETAILS_TABLE_ENTITY_FULL = "t_Metric_Summary_Full";
        private const string REPORT_ENTITY_DETAILS_TABLE_ENTITY_HOURLY = "t_Metric_Summary_Hourly";
        // Description tables from metric.summary.csv
        private const string REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_DESCRIPTION = "t_Metric_Description_{0}_{1}";
        // Metric data tables from metric.values.csv
        private const string REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_VALUES = "t_Metric_Values_{0}_{1}";
        // Grid data
        private const string REPORT_ENTITY_DETAILS_ACTIVITY_GRID = "t_Activity";
        // Events from events.csv and hrviolationevents.csv
        private const string REPORT_ENTITY_DETAILS_TABLE_EVENTS = "t_Events";
        private const string REPORT_ENTITY_DETAILS_TABLE_HEALTH_RULE_VIOLATION_EVENTS = "t_EventsHealthRuleViolations";
        // Snapshot data
        private const string REPORT_ENTITY_DETAILS_TABLE_SNAPSHOTS = "t_Snapshots";
        private const string REPORT_ENTITY_DETAILS_TABLE_SEGMENTS = "t_Segment";
        private const string REPORT_ENTITY_DETAILS_TABLE_EXIT_CALLS = "t_ExitCalls";
        private const string REPORT_ENTITY_DETAILS_TABLE_SERVICE_ENDPOINT_CALLS = "t_ServiceEndpointCalls";
        private const string REPORT_ENTITY_DETAILS_TABLE_DETECTED_ERRORS = "t_DetectedErrors";
        private const string REPORT_ENTITY_DETAILS_TABLE_BUSINESS_DATA = "t_BusinessData";

        // Hourly graph data
        private const string REPORT_ENTITY_DETAILS_GRAPH = "g_M_{0}_{1:yyyyMMddHHss}";

        private const string REPORT_ENTITY_DETAILS_PIVOT_EVENTS = "p_Events";
        private const string REPORT_ENTITY_DETAILS_PIVOT_HEALTH_RULE_VIOLATION_EVENTS = "p_Events_HR";

        private const string REPORT_ENTITY_DETAILS_PIVOT_SNAPSHOTS = "p_Snapshots";
        private const string REPORT_ENTITY_DETAILS_PIVOT_SEGMENTS = "p_Segments";
        private const string REPORT_ENTITY_DETAILS_PIVOT_EXIT_CALLS = "p_ExitCalls";
        private const string REPORT_ENTITY_DETAILS_PIVOT_DETECTED_ERRORS = "p_DetectedErrors";
        private const string REPORT_ENTITY_DETAILS_PIVOT_BUSINESS_DATA = "p_BusinessData";

        private const int REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT = 4;
        private const int REPORT_ENTITY_DETAILS_GRAPHS_SHEET_START_TABLE_AT = 15;
        private const int REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT = 6;

        private const string REPORT_ENTITY_DETAILS_TABLE_EVENTS_IN_TIMELINE = "t_EventsTimelineHeaders";
        private const string REPORT_ENTITY_DETAILS_TABLE_SNAPSHOTS_IN_TIMELINE = "t_SnapshotsTimelineHeaders";

        #endregion

        #region Constants for Snapshots Report contents

        private const string REPORT_SNAPSHOTS_SHEET_CONTROLLERS_LIST = "3.Controllers";

        private const string REPORT_SNAPSHOTS_SHEET_SNAPSHOTS = "4.Snapshots";
        private const string REPORT_SNAPSHOTS_SHEET_SNAPSHOTS_PIVOT = "4.Snapshots.Type";

        private const string REPORT_SNAPSHOTS_SHEET_SEGMENTS = "5.Segments";
        private const string REPORT_SNAPSHOTS_SHEET_SEGMENTS_PIVOT = "5.Segments.Type";

        private const string REPORT_SNAPSHOTS_SHEET_EXIT_CALLS = "6.Exit Calls";
        private const string REPORT_SNAPSHOTS_SHEET_EXIT_CALLS_PIVOT = "6.Exit Calls.Type";

        private const string REPORT_SNAPSHOTS_SHEET_SERVICE_ENDPOINT_CALLS = "7.Service Endpoint Calls";

        private const string REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS = "8.Errors";
        private const string REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS_PIVOT = "8.Errors.Type";

        private const string REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA = "9.Business Data";
        private const string REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA_PIVOT = "9.Business Data.Type";

        private const string REPORT_SNAPSHOTS_TABLE_SNAPSHOTS = "t_Snapshots";
        private const string REPORT_SNAPSHOTS_TABLE_SEGMENTS = "t_Segments";
        private const string REPORT_SNAPSHOTS_TABLE_EXIT_CALLS = "t_ExitCalls";
        private const string REPORT_SNAPSHOTS_TABLE_SERVICE_ENDPOINT_CALLS = "t_ServiceEndpointCalls";
        private const string REPORT_SNAPSHOTS_TABLE_DETECTED_ERRORS = "t_DetectedErrors";
        private const string REPORT_SNAPSHOTS_TABLE_BUSINESS_DATA = "t_BusinessData";

        private const string REPORT_SNAPSHOTS_PIVOT_SNAPSHOTS = "p_Snapshots";
        private const string REPORT_SNAPSHOTS_PIVOT_SEGMENTS = "p_Segments";
        private const string REPORT_SNAPSHOTS_PIVOT_EXIT_CALLS = "p_ExitCalls";
        private const string REPORT_SNAPSHOTS_PIVOT_DETECTED_ERRORS = "p_DetectedErrors";
        private const string REPORT_SNAPSHOTS_PIVOT_BUSINESS_DATA = "p_BusinessData";

        private const int REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT = 4;
        private const int REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT = 8;

        #endregion

        #region Constants for Deeplinks

        private const string DEEPLINK_CONTROLLER = @"{0}/controller/#/location=AD_HOME_OVERVIEW&timeRange={1}";
        private const string DEEPLINK_APPLICATION = @"{0}/controller/#/location=APP_DASHBOARD&timeRange={2}&application={1}&dashboardMode=force";
        private const string DEEPLINK_TIER = @"{0}/controller/#/location=APP_COMPONENT_MANAGER&timeRange={3}&application={1}&component={2}&dashboardMode=force";
        private const string DEEPLINK_NODE = @"{0}/controller/#/location=APP_NODE_MANAGER&timeRange={3}&application={1}&node={2}&dashboardMode=force";
        private const string DEEPLINK_BACKEND = @"{0}/controller/#/location=APP_BACKEND_DASHBOARD&timeRange={3}&application={1}&backendDashboard={2}&dashboardMode=force";
        private const string DEEPLINK_BUSINESS_TRANSACTION = @"{0}/controller/#/location=APP_BT_DETAIL&timeRange={3}&application={1}&businessTransaction={2}&dashboardMode=force";
        private const string DEEPLINK_SERVICE_ENDPOINT = @"{0}/controller/#/location=APP_SERVICE_ENDPOINT_DETAIL&timeRange={4}&application={1}&component={2}&serviceEndpoint={3}";
        private const string DEEPLINK_ERROR = @"{0}/controller/#/location=APP_ERROR_DASHBOARD&timeRange={3}&application={1}&error={2}";
        private const string DEEPLINK_APPLICATION_MOBILE = @"{0}/controller/#/location=EUM_MOBILE_MAIN_DASHBOARD&timeRange={3}&application={1}&mobileApp={2}";
        private const string DEEPLINK_HEALTH_RULE = @"{0}/controller/#/location=ALERT_RESPOND_HEALTH_RULES&timeRange={3}&application={1}";
        private const string DEEPLINK_INCIDENT = @"{0}/controller/#/location=APP_INCIDENT_DETAIL_MODAL&timeRange={4}&application={1}&incident={2}&incidentTime={3}";
        private const string DEEPLINK_SNAPSHOT_OVERVIEW = @"{0}/controller/#/location=APP_SNAPSHOT_VIEWER&rsdTime={3}&application={1}&requestGUID={2}&tab=overview&dashboardMode=force";
        private const string DEEPLINK_SNAPSHOT_SEGMENT = @"{0}/controller/#/location=APP_SNAPSHOT_VIEWER&rsdTime={4}&application={1}&requestGUID={2}&tab={3}&dashboardMode=force";

        private const string DEEPLINK_METRIC = @"{0}/controller/#/location=METRIC_BROWSER&timeRange={3}&application={1}&metrics={2}";
        private const string DEEPLINK_TIMERANGE_LAST_15_MINUTES = "last_15_minutes.BEFORE_NOW.-1.-1.15";
        private const string DEEPLINK_TIMERANGE_BETWEEN_TIMES = "Custom_Time_Range.BETWEEN_TIMES.{0}.{1}.{2}";
        private const string DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID = "APPLICATION.{0}.{1}";
        private const string DEEPLINK_METRIC_TIER_TARGET_METRIC_ID = "APPLICATION_COMPONENT.{0}.{1}";
        private const string DEEPLINK_METRIC_NODE_TARGET_METRIC_ID = "APPLICATION_COMPONENT_NODE.{0}.{1}";

        #endregion

        #region Constants for parallelization of processes

        private const int METRIC_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD = 10;
        private const int METRIC_EXTRACT_NUMBER_OF_THREADS = 5;

        private const int FLOWMAP_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD = 20;
        private const int FLOWMAP_EXTRACT_NUMBER_OF_THREADS = 5;

        private const int SNAPSHOTS_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD = 50;
        private const int SNAPSHOTS_EXTRACT_NUMBER_OF_THREADS = 10;

        private const int SNAPSHOTS_INDEX_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD = 100;
        private const int SNAPSHOTS_INDEX_NUMBER_OF_THREADS = 10;

        private const int METRIC_DETAILS_REPORT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD = 10;
        private const int METRIC_DETAILS_REPORT_EXTRACT_NUMBER_OF_THREADS = 10;

        #endregion

        #region Job steps router

        internal static void startOrContinueJob(ProgramOptions programOptions)
        {
            JobConfiguration jobConfiguration = FileIOHelper.readJobConfigurationFromFile(programOptions.OutputJobFilePath);
            if (jobConfiguration == null)
            {
                loggerConsole.Error("Unable to load job input file {0}", programOptions.InputJobFilePath);

                return;
            }

            List<JobStatus> jobSteps = new List<JobStatus>
            {
                // Get data
                JobStatus.ExtractControllerApplicationsAndEntities,
                JobStatus.ExtractControllerAndApplicationConfiguration,
                JobStatus.ExtractApplicationAndEntityMetrics,
                JobStatus.ExtractApplicationAndEntityFlowmaps,
                JobStatus.ExtractSnapshots,
                JobStatus.ExtractEvents,

                // Process data
                JobStatus.IndexControllersApplicationsAndEntities,
                JobStatus.IndexControllerAndApplicationConfiguration,
                JobStatus.IndexApplicationAndEntityMetrics,
                JobStatus.IndexApplicationAndEntityFlowmaps,
                JobStatus.IndexSnapshots,
                JobStatus.IndexEvents,

                // Report data
                JobStatus.ReportControlerApplicationsAndEntities,
                JobStatus.ReportControllerAndApplicationConfiguration,
                JobStatus.ReportEventsAndHealthRuleViolations,
                JobStatus.ReportApplicationAndEntityMetrics,
                JobStatus.ReportSnapshots,
                JobStatus.ReportIndividualApplicationAndEntityDetails,
                //JobStatus.ReportFlameGraphs,

                // Done 
                JobStatus.Done,

                JobStatus.Error
            };
            LinkedList<JobStatus> jobStepsLinked = new LinkedList<JobStatus>(jobSteps);

            #region Output diagnostic parameters to log

            loggerConsole.Info("Job status {0}({0:d})", jobConfiguration.Status);
            logger.Info("Job status {0}({0:d})", jobConfiguration.Status);
            logger.Info("Job input: TimeRange.From='{0:o}', TimeRange.To='{1:o}', ExpandedTimeRange.From='{2:o}', ExpandedTimeRange.To='{3:o}', Time ranges='{4}', Flowmaps='{5}', Metrics='{6}', Snapshots='{7}', Configuration='{8}'", jobConfiguration.Input.TimeRange.From, jobConfiguration.Input.TimeRange.To, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To, jobConfiguration.Input.HourlyTimeRanges.Count, jobConfiguration.Input.Flowmaps, jobConfiguration.Input.Metrics, jobConfiguration.Input.Snapshots, jobConfiguration.Input.Configuration);

            foreach (JobTimeRange jobTimeRange in jobConfiguration.Input.HourlyTimeRanges)
            {
                logger.Info("Expanded time ranges: From='{0:o}', To='{1:o}'", jobTimeRange.From, jobTimeRange.To);
            }

            #endregion

            // Run the step and move to next until things are done
            while (jobConfiguration.Status != JobStatus.Done && jobConfiguration.Status != JobStatus.Error)
            {
                switch (jobConfiguration.Status)
                {
                    case JobStatus.ExtractControllerApplicationsAndEntities:
                        if (stepExtractControllerApplicationsAndEntities(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                        }
                        else
                        {
                            jobConfiguration.Status = JobStatus.Error;
                        }
                        break;

                    case JobStatus.ExtractControllerAndApplicationConfiguration:
                        if (jobConfiguration.Input.Configuration == true)
                        {
                            if (stepExtractControllerAndApplicationConfiguration(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping export of configuration");
                        }
                        break;

                    case JobStatus.ExtractApplicationAndEntityMetrics:
                        if (jobConfiguration.Input.Metrics == true)
                        {
                            if (stepExtractApplicationAndEntityMetrics(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping export of entity metrics");
                        }
                        break;

                    case JobStatus.ExtractApplicationAndEntityFlowmaps:
                        if (jobConfiguration.Input.Flowmaps == true)
                        {
                            if (stepExtractApplicationAndEntityFlowmaps(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping export of entity flowmaps");
                        }
                        break;

                    case JobStatus.ExtractSnapshots:
                        if (jobConfiguration.Input.Snapshots == true)
                        {
                            if (stepExtractSnapshots(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping export of snapshots");
                        }
                        break;

                    case JobStatus.ExtractEvents:
                        if (jobConfiguration.Input.Events == true)
                        {
                            if (stepExtractEvents(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping export of events");
                        }
                        break;

                    case JobStatus.IndexControllersApplicationsAndEntities:
                        if (stepIndexControllersApplicationsAndEntities(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                        }
                        else
                        {
                            jobConfiguration.Status = JobStatus.Error;
                        }
                        break;

                    case JobStatus.IndexControllerAndApplicationConfiguration:
                        if (jobConfiguration.Input.Configuration == true)
                        {
                            if (stepIndexControllerAndApplicationConfiguration(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping index of configuration");
                        }
                        break;

                    case JobStatus.IndexApplicationAndEntityMetrics:
                        if (jobConfiguration.Input.Metrics == true)
                        {
                            if (stepIndexApplicationAndEntityMetrics(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping index of entity metrics");
                        }
                        break;

                    case JobStatus.IndexApplicationAndEntityFlowmaps:
                        if (jobConfiguration.Input.Flowmaps == true)
                        {
                            if (stepIndexApplicationAndEntityFlowmaps(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping index of entity flowmaps");
                        }
                        break;

                    case JobStatus.IndexSnapshots:
                        if (jobConfiguration.Input.Snapshots == true)
                        {
                            if (stepIndexSnapshots(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping index of snapshots");
                        }
                        break;

                    case JobStatus.IndexEvents:
                        if (jobConfiguration.Input.Events == true)
                        {
                            if (stepIndexEvents(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping index of events");
                        }
                        break;

                    case JobStatus.ReportControlerApplicationsAndEntities:
                        if (stepReportControlerApplicationsAndEntities(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                        }
                        break;

                    case JobStatus.ReportControllerAndApplicationConfiguration:
                        if (jobConfiguration.Input.Configuration == true)
                        {
                            if (stepReportControllerAndApplicationConfiguration(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping report of configuration");
                        }
                        break;

                    case JobStatus.ReportEventsAndHealthRuleViolations:
                        if (jobConfiguration.Input.Events == true)
                        {
                            if (stepReportEventsAndHealthRuleViolations(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping report of events and health rule violations");
                        }
                        break;

                    case JobStatus.ReportApplicationAndEntityMetrics:
                        if (jobConfiguration.Input.Metrics == true)
                        {
                            if (stepReportApplicationAndEntityMetrics(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping report of entity metrics");
                        }
                        break;

                    case JobStatus.ReportIndividualApplicationAndEntityDetails:
                        if (jobConfiguration.Input.Metrics == true ||
                            jobConfiguration.Input.Events == true ||
                            jobConfiguration.Input.Flowmaps == true ||
                            jobConfiguration.Input.Snapshots == true)
                        {
                            if (stepReportIndividualApplicationAndEntityDetails(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping report of entity metric details");
                        }
                        break;

                    case JobStatus.ReportSnapshots:
                        if (jobConfiguration.Input.Snapshots == true)
                        {
                            if (stepReportSnapshots(programOptions, jobConfiguration, jobConfiguration.Status) == true)
                            {
                                jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;
                            }
                            else
                            {
                                jobConfiguration.Status = JobStatus.Error;
                            }
                        }
                        else
                        {
                            jobConfiguration.Status = jobStepsLinked.Find(jobConfiguration.Status).Next.Value;

                            loggerConsole.Debug("Skipping report of snapshots");
                        }
                        break;

                    default:
                        jobConfiguration.Status = JobStatus.Error;
                        break;
                }

                // Save the resulting JSON file to the job target folder
                if (FileIOHelper.writeJobConfigurationToFile(jobConfiguration, programOptions.OutputJobFilePath) == false)
                {
                    loggerConsole.Error("Unable to write job input file {0}", programOptions.OutputJobFilePath);

                    return;
                }
            }
        }

        #endregion

        #region Extract steps

        private static bool stepExtractControllerApplicationsAndEntities(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);

                        // Entity files
                        string applicationsFilePath = Path.Combine(controllerFolderPath, EXTRACT_ENTITY_APPLICATIONS_FILE_NAME);
                        string applicationFilePath = Path.Combine(applicationFolderPath, EXTRACT_ENTITY_APPLICATION_FILE_NAME);
                        string tiersFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_TIERS_FILE_NAME);
                        string nodesFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_NODES_FILE_NAME);
                        string backendsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BACKENDS_FILE_NAME);
                        string backendsDetailFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BACKENDS_DETAIL_FILE_NAME);
                        string businessTransactionsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string serviceEndPointsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string serviceEndPointsDetailFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_SERVICE_ENDPOINTS_DETAIL_FILE_NAME);
                        string errorsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_ERRORS_FILE_NAME);

                        #endregion

                        #region Applications

                        // Only do it once per controller, if processing multiple applications
                        if (File.Exists(applicationsFilePath) != true)
                        {
                            loggerConsole.Info("List of Applications");

                            string applicationsJSON = controllerApi.GetListOfApplications();
                            if (applicationsJSON != String.Empty) FileIOHelper.saveFileToFolder(applicationsJSON, applicationsFilePath);
                        }

                        #endregion

                        #region Application

                        loggerConsole.Info("This Application");

                        string applicationJSON = controllerApi.GetSingleApplication(jobTarget.ApplicationID);
                        if (applicationJSON != String.Empty) FileIOHelper.saveFileToFolder(applicationJSON, applicationFilePath);

                        #endregion

                        #region Tiers

                        loggerConsole.Info("List of Tiers");

                        string tiersJSON = controllerApi.GetListOfTiers(jobTarget.ApplicationID);
                        if (tiersJSON != String.Empty) FileIOHelper.saveFileToFolder(tiersJSON, tiersFilePath);

                        #endregion

                        #region Nodes

                        loggerConsole.Info("List of Nodes");

                        string nodesJSON = controllerApi.GetListOfNodes(jobTarget.ApplicationID);
                        if (nodesJSON != String.Empty) FileIOHelper.saveFileToFolder(nodesJSON, nodesFilePath);

                        #endregion

                        #region Backends

                        loggerConsole.Info("List of Backends");

                        string backendsJSON = controllerApi.GetListOfBackends(jobTarget.ApplicationID);
                        if (backendsJSON != String.Empty) FileIOHelper.saveFileToFolder(backendsJSON, backendsFilePath);

                        controllerApi.PrivateApiLogin();
                        backendsJSON = controllerApi.GetListOfBackendsAdditionalDetail(jobTarget.ApplicationID);
                        if (backendsJSON != String.Empty) FileIOHelper.saveFileToFolder(backendsJSON, backendsDetailFilePath);
                        
                        #endregion

                        #region Business Transactions

                        loggerConsole.Info("List of Business Transactions");

                        string businessTransactionsJSON = controllerApi.GetListOfBusinessTransactions(jobTarget.ApplicationID);
                        if (businessTransactionsJSON != String.Empty) FileIOHelper.saveFileToFolder(businessTransactionsJSON, businessTransactionsFilePath);

                        #endregion

                        #region Service Endpoints

                        loggerConsole.Info("List of Service Endpoints");

                        string serviceEndPointsJSON = controllerApi.GetListOfServiceEndpoints(jobTarget.ApplicationID);
                        if (serviceEndPointsJSON != String.Empty) FileIOHelper.saveFileToFolder(serviceEndPointsJSON, serviceEndPointsFilePath);

                        controllerApi.PrivateApiLogin();
                        serviceEndPointsJSON = controllerApi.GetListOfServiceEndpointsAdditionalDetail(jobTarget.ApplicationID);
                        if (serviceEndPointsJSON != String.Empty) FileIOHelper.saveFileToFolder(serviceEndPointsJSON, serviceEndPointsDetailFilePath);

                        #endregion

                        #region Errors

                        loggerConsole.Info("List of Errors");

                        string errorsJSON = controllerApi.GetListOfErrors(jobTarget.ApplicationID);
                        if (errorsJSON != String.Empty) FileIOHelper.saveFileToFolder(errorsJSON, errorsFilePath);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepExtractControllerAndApplicationConfiguration(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string configFolderPath = Path.Combine(applicationFolderPath, CONFIGURATION_FOLDER_NAME);

                        // Entity files
                        string applicationsFilePath = Path.Combine(controllerFolderPath, EXTRACT_ENTITY_APPLICATIONS_FILE_NAME);
                        string applicationConfigFilePath = Path.Combine(configFolderPath, EXTRACT_CONFIGURATION_APPLICATION_FILE_NAME);
                        string controllerSettingsFilePath = Path.Combine(controllerFolderPath, EXTRACT_CONFIGURATION_CONTROLLER_FILE_NAME);

                        #endregion

                        #region Controller

                        if (File.Exists(controllerSettingsFilePath) != true)
                        {
                            loggerConsole.Info("Controller Settings");

                            string controllerSettingsJSON = controllerApi.GetControllerConfiguration();
                            if (controllerSettingsJSON != String.Empty) FileIOHelper.saveFileToFolder(controllerSettingsJSON, controllerSettingsFilePath);
                        }

                        #endregion

                        #region Application

                        loggerConsole.Info("Application Configuration");

                        // Application configuration
                        string applicationConfigXml = controllerApi.GetApplicationConfiguration(jobTarget.ApplicationID);
                        if (applicationConfigXml != String.Empty) FileIOHelper.saveFileToFolder(applicationConfigXml, applicationConfigFilePath);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepExtractApplicationAndEntityMetrics(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string metricsFolderPath = Path.Combine(applicationFolderPath, METRICS_FOLDER_NAME);

                        // Entity files
                        string tiersFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_TIERS_FILE_NAME);
                        string nodesFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_NODES_FILE_NAME);
                        string backendsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BACKENDS_FILE_NAME);
                        string businessTransactionsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string serviceEndPointsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string errorsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_ERRORS_FILE_NAME);

                        #endregion

                        #region Application

                        // Application
                        loggerConsole.Info("Extract Metrics for Application ({0} entities * {1} time ranges * {2} metrics)", 1, jobConfiguration.Input.HourlyTimeRanges.Count + 1, 5);

                        extractMetricsApplication(jobConfiguration, jobTarget, controllerApi, metricsFolderPath);

                        #endregion

                        #region Tiers

                        List<AppDRESTTier> tiersList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTTier>(tiersFilePath);
                        if (tiersList != null)
                        {
                            loggerConsole.Info("Extract Metrics for Tiers ({0} entities * {1} time ranges * {2} metrics)", tiersList.Count, jobConfiguration.Input.HourlyTimeRanges.Count + 1, 5);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var tiersListChunks = tiersList.BreakListIntoChunks(METRIC_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTTier>, int>(
                                    tiersListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (tiersListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractMetricsTiers(jobConfiguration, jobTarget, controllerApi, tiersListChunk, metricsFolderPath, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractMetricsTiers(jobConfiguration, jobTarget, controllerApi, tiersList, metricsFolderPath, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Nodes

                        List<AppDRESTNode> nodesList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTNode>(nodesFilePath);
                        if (nodesList != null)
                        {
                            loggerConsole.Info("Extract Metrics for Nodes ({0} entities * {1} time ranges * {2} metrics)", nodesList.Count, jobConfiguration.Input.HourlyTimeRanges.Count + 1, 5);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var nodesListChunks = nodesList.BreakListIntoChunks(METRIC_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTNode>, int>(
                                    nodesListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (nodesListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractMetricsNodes(jobConfiguration, jobTarget, controllerApi, nodesListChunk, metricsFolderPath, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractMetricsNodes(jobConfiguration, jobTarget, controllerApi, nodesList, metricsFolderPath, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Backends

                        List<AppDRESTBackend> backendsList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTBackend>(backendsFilePath);
                        if (backendsList != null)
                        {
                            loggerConsole.Info("Extract Metrics for Backends ({0} entities * {1} time ranges * {2} metrics)", backendsList.Count, jobConfiguration.Input.HourlyTimeRanges.Count + 1, 3);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var backendsListChunks = backendsList.BreakListIntoChunks(METRIC_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTBackend>, int>(
                                    backendsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (backendsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractMetricsBackends(jobConfiguration, jobTarget, controllerApi, backendsListChunk, metricsFolderPath, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractMetricsBackends(jobConfiguration, jobTarget, controllerApi, backendsList, metricsFolderPath, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Business Transactions

                        List<AppDRESTBusinessTransaction> businessTransactionsList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTBusinessTransaction>(businessTransactionsFilePath);
                        if (businessTransactionsList != null)
                        {
                            loggerConsole.Info("Extract Metrics for Business Transactions ({0} entities * {1} time ranges * {2} metrics)", businessTransactionsList.Count, jobConfiguration.Input.HourlyTimeRanges.Count + 1, 3);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var businessTransactionsListChunks = businessTransactionsList.BreakListIntoChunks(METRIC_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTBusinessTransaction>, int>(
                                    businessTransactionsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (businessTransactionsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractMetricsBusinessTransactions(jobConfiguration, jobTarget, controllerApi, businessTransactionsListChunk, metricsFolderPath, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractMetricsBusinessTransactions(jobConfiguration, jobTarget, controllerApi, businessTransactionsList, metricsFolderPath, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Service Endpoints

                        List<AppDRESTMetric> serviceEndpointsList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(serviceEndPointsFilePath);
                        if (serviceEndpointsList != null)
                        {
                            loggerConsole.Info("Extract Metrics for Service Endpoints ({0} entities * {1} time ranges * {2} metrics)", serviceEndpointsList.Count, jobConfiguration.Input.HourlyTimeRanges.Count + 1, 3);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var serviceEndpointsListChunks = serviceEndpointsList.BreakListIntoChunks(METRIC_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTMetric>, int>(
                                    serviceEndpointsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (serviceEndpointsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractMetricsServiceEndpoints(jobConfiguration, jobTarget, controllerApi, serviceEndpointsListChunk, tiersList, metricsFolderPath, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractMetricsServiceEndpoints(jobConfiguration, jobTarget, controllerApi, serviceEndpointsList, tiersList, metricsFolderPath, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Errors

                        List<AppDRESTMetric> errorsList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(errorsFilePath);
                        if (errorsList != null)
                        {
                            loggerConsole.Info("Extract Metrics for Errors ({0} entities * {1} time ranges * {2} metrics)", errorsList.Count, jobConfiguration.Input.HourlyTimeRanges.Count + 1, 1);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var errorsListChunks = errorsList.BreakListIntoChunks(METRIC_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTMetric>, int>(
                                    errorsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (errorsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractMetricsErrors(jobConfiguration, jobTarget, controllerApi, errorsListChunk, tiersList, metricsFolderPath, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractMetricsErrors(jobConfiguration, jobTarget, controllerApi, errorsList, tiersList, metricsFolderPath, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepExtractApplicationAndEntityFlowmaps(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string metricsFolderPath = Path.Combine(applicationFolderPath, METRICS_FOLDER_NAME);

                        // Entity files
                        string tiersFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_TIERS_FILE_NAME);
                        string nodesFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_NODES_FILE_NAME);
                        string backendsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BACKENDS_FILE_NAME);
                        string businessTransactionsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string serviceEndPointsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);

                        #endregion

                        // Login into private API
                        controllerApi.PrivateApiLogin();

                        #region Prepare time range

                        long fromTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.From);
                        long toTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.To);
                        long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);

                        #endregion

                        #region Application

                        loggerConsole.Info("Extract Flowmap for Application");

                        extractFlowmapsApplication(jobConfiguration, jobTarget, controllerApi, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes);

                        #endregion

                        #region Tiers

                        List<AppDRESTTier> tiersList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTTier>(tiersFilePath);
                        if (tiersList != null)
                        {
                            loggerConsole.Info("Extract Flowmaps for Tiers ({0} entities)", tiersList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var tiersListChunks = tiersList.BreakListIntoChunks(FLOWMAP_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTTier>, int>(
                                    tiersListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = FLOWMAP_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (tiersListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractFlowmapsTiers(jobConfiguration, jobTarget, controllerApi, tiersListChunk, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractFlowmapsTiers(jobConfiguration, jobTarget, controllerApi, tiersList, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Nodes

                        List<AppDRESTNode> nodesList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTNode>(nodesFilePath);
                        if (nodesList != null)
                        {
                            loggerConsole.Info("Extract Flowmaps for Nodes ({0} entities)", nodesList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var nodesListChunks = nodesList.BreakListIntoChunks(FLOWMAP_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTNode>, int>(
                                    nodesListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = FLOWMAP_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (nodesListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractFlowmapsNodes(jobConfiguration, jobTarget, controllerApi, nodesListChunk, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractFlowmapsNodes(jobConfiguration, jobTarget, controllerApi, nodesList, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Backends

                        List<AppDRESTBackend> backendsList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTBackend>(backendsFilePath);
                        if (backendsList != null)
                        {
                            loggerConsole.Info("Extract Flowmaps for Backends ({0} entities)", backendsList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var backendsListChunks = backendsList.BreakListIntoChunks(FLOWMAP_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTBackend>, int>(
                                    backendsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = FLOWMAP_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (backendsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractFlowmapsBackends(jobConfiguration, jobTarget, controllerApi, backendsListChunk, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = extractFlowmapsBackends(jobConfiguration, jobTarget, controllerApi, backendsList, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Business Transactions

                        List<AppDRESTBusinessTransaction> businessTransactionsList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTBusinessTransaction>(businessTransactionsFilePath);
                        if (businessTransactionsList != null)
                        {
                            loggerConsole.Info("Extract Flowmaps for Business Transactions ({0} entities)", businessTransactionsList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var businessTransactionsListChunks = businessTransactionsList.BreakListIntoChunks(FLOWMAP_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<AppDRESTBusinessTransaction>, int>(
                                    businessTransactionsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = FLOWMAP_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (businessTransactionsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += extractFlowmapsBusinessTransactions(jobConfiguration, jobTarget, controllerApi, businessTransactionsListChunk, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            {
                                j = extractFlowmapsBusinessTransactions(jobConfiguration, jobTarget, controllerApi, businessTransactionsList, metricsFolderPath, fromTimeUnix, toTimeUnix, differenceInMinutes, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepExtractSnapshots(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string snapshotsFolderPath = Path.Combine(applicationFolderPath, SNAPSHOTS_FOLDER_NAME);

                        // Entity files
                        string tiersFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_TIERS_FILE_NAME);
                        string businessTransactionsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);

                        #endregion

                        #region List of Snapshots in time ranges

                        // Login into private API
                        controllerApi.PrivateApiLogin();

                        loggerConsole.Info("Extract List of Snapshots ({0} time ranges)", jobConfiguration.Input.HourlyTimeRanges.Count);

                        // Get list of snapshots in each time range
                        int totalSnapshotsFound = 0;
                        foreach (JobTimeRange jobTimeRange in jobConfiguration.Input.HourlyTimeRanges)
                        {
                            loggerConsole.Info("Extract List of Snapshots from {0:o} to {1:o}", jobTimeRange.From, jobTimeRange.To);

                            string snapshotsFilePath = Path.Combine(snapshotsFolderPath, String.Format(EXTRACT_SNAPSHOTS_FILE_NAME, jobTimeRange.From, jobTimeRange.To));

                            int differenceInMinutes = (int)(jobTimeRange.To - jobTimeRange.From).TotalMinutes;

                            if (File.Exists(snapshotsFilePath) == false)
                            {
                                JArray listOfSnapshots = new JArray();

                                // Extract snapshot list
                                long serverCursorId = 0;
                                string serverCursorIdName = "rsdScrollId";
                                do
                                {
                                    string snapshotsJSON = controllerApi.GetListOfSnapshots(jobTarget.ApplicationID, jobTimeRange.From, jobTimeRange.To, differenceInMinutes, SNAPSHOTS_QUERY_PAGE_SIZE, serverCursorIdName, serverCursorId);

                                    if (snapshotsJSON == String.Empty)
                                    {
                                        // No snapshots in this page, exit
                                        serverCursorId = 0;
                                    }
                                    else
                                    {
                                        Console.Write(".");

                                        // Load snapshots
                                        JObject snapshotsParsed = JObject.Parse(snapshotsJSON);
                                        JArray snapshots = (JArray)snapshotsParsed["requestSegmentDataListItems"];
                                        foreach (JObject snapshot in snapshots)
                                        {
                                            listOfSnapshots.Add(snapshot);
                                        }

                                        // If there are more snapshots on the server, the server cursor would be non-0 
                                        object serverCursorIdObj = snapshotsParsed["serverCursor"]["rsdScrollId"];
                                        if (serverCursorIdObj == null)
                                        {
                                            // Sometimes - >4.3.3? the value of scroll is in scrollId, not rsdScrollId
                                            serverCursorIdObj = snapshotsParsed["serverCursor"]["scrollId"];
                                            if (serverCursorIdObj != null)
                                            {
                                                // And the name of the cursor changes too
                                                serverCursorIdName = "scrollId";
                                            }
                                            else
                                            {
                                                serverCursorId = 0;
                                            }
                                        }
                                        if (serverCursorIdObj != null)
                                        {
                                            serverCursorId = -1;
                                            Int64.TryParse(serverCursorIdObj.ToString(), out serverCursorId);
                                        }

                                        logger.Info("Retrieved snapshots from Controller {0}, Application {1}, From {2:o}, To {3:o}', number of snapshots {4}, continuation CursorId {5}", jobTarget.Controller, jobTarget.Application, jobTimeRange.From, jobTimeRange.To, snapshots.Count, serverCursorId);

                                        Console.Write("+{0}", listOfSnapshots.Count);
                                    }
                                }
                                while (serverCursorId > 0);

                                Console.WriteLine();

                                FileIOHelper.writeJArrayToFile(listOfSnapshots, snapshotsFilePath);

                                totalSnapshotsFound = totalSnapshotsFound + listOfSnapshots.Count;

                                logger.Info("{0} snapshots from {1:o} to {2:o}", listOfSnapshots.Count, jobTimeRange.From, jobTimeRange.To);
                                loggerConsole.Info("{0} snapshots from {1:o} to {2:o}", listOfSnapshots.Count, jobTimeRange.From, jobTimeRange.To);
                            }
                        }

                        logger.Info("{0} snapshots in all time ranges", totalSnapshotsFound);
                        loggerConsole.Info("{0} snapshots in all time ranges", totalSnapshotsFound);

                        #endregion

                        #region Individual Snapshots

                        // Extract individual snapshots
                        loggerConsole.Info("Extract Individual Snapshots");

                        // Process each hour at a time
                        foreach (JobTimeRange jobTimeRange in jobConfiguration.Input.HourlyTimeRanges)
                        {
                            string snapshotsFilePath = Path.Combine(snapshotsFolderPath, String.Format(EXTRACT_SNAPSHOTS_FILE_NAME, jobTimeRange.From, jobTimeRange.To));
                            JArray listOfSnapshotsInHour = FileIOHelper.loadJArrayFromFile(snapshotsFilePath);
                            if (listOfSnapshotsInHour != null && listOfSnapshotsInHour.Count > 0)
                            {
                                loggerConsole.Info("Extract Snapshots {0:o} to {1:o} ({2} snapshots)", jobTimeRange.From, jobTimeRange.To, listOfSnapshotsInHour.Count);

                                int j = 0;

                                if (programOptions.ProcessSequentially == false)
                                {
                                    var listOfSnapshotsInHourChunks = listOfSnapshotsInHour.BreakListIntoChunks(SNAPSHOTS_EXTRACT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                    Parallel.ForEach<List<JToken>, int>(
                                        listOfSnapshotsInHourChunks,
                                        new ParallelOptions { MaxDegreeOfParallelism = SNAPSHOTS_EXTRACT_NUMBER_OF_THREADS },
                                        () => 0,
                                        (listOfSnapshotsInHourChunk, loop, subtotal) =>
                                        {
                                            subtotal += extractSnapshots(jobConfiguration, jobTarget, controllerApi, listOfSnapshotsInHourChunk, snapshotsFolderPath, false);
                                            return subtotal;
                                        },
                                        (finalResult) =>
                                        {
                                            j = Interlocked.Add(ref j, finalResult);
                                            Console.Write("[{0}].", j);
                                        }
                                    );
                                }
                                else
                                {
                                    j = extractSnapshots(jobConfiguration, jobTarget, controllerApi, listOfSnapshotsInHour.ToList<JToken>(), snapshotsFolderPath, true);
                                }

                                loggerConsole.Info("{0} snapshots", j);
                            }
                        }

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepExtractEvents(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string eventsFolderPath = Path.Combine(applicationFolderPath, EVENTS_FOLDER_NAME);

                        #endregion

                        #region Health Rule violations

                        loggerConsole.Info("Extract List of Health Rule Violations ({0} time ranges)", jobConfiguration.Input.HourlyTimeRanges.Count);

                        JArray listOfHealthRuleViolations = new JArray();
                        int totalHealthRuleViolationsFound = 0;
                        foreach (JobTimeRange jobTimeRange in jobConfiguration.Input.HourlyTimeRanges)
                        {
                            long fromTimeUnix = convertToUnixTimestamp(jobTimeRange.From);
                            long toTimeUnix = convertToUnixTimestamp(jobTimeRange.To);

                            string healthRuleViolationsJSON = controllerApi.GetHealthRuleViolations(jobTarget.ApplicationID, fromTimeUnix, toTimeUnix);
                            if (healthRuleViolationsJSON != String.Empty)
                            {
                                Console.Write(".");

                                // Load health rule violations
                                JArray healthRuleViolationsInHour = JArray.Parse(healthRuleViolationsJSON);
                                foreach (JObject healthRuleViolation in healthRuleViolationsInHour)
                                {
                                    listOfHealthRuleViolations.Add(healthRuleViolation);
                                }
                                totalHealthRuleViolationsFound = totalHealthRuleViolationsFound + healthRuleViolationsInHour.Count;
                                Console.Write("+{0}", healthRuleViolationsInHour.Count);
                            }
                        }

                        Console.WriteLine();

                        if (listOfHealthRuleViolations.Count > 0)
                        {
                            string healthRuleViolationsFilePath = Path.Combine(
                                eventsFolderPath, 
                                String.Format(HEALTH_RULE_VIOLATIONS_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                            FileIOHelper.writeJArrayToFile(listOfHealthRuleViolations, healthRuleViolationsFilePath);

                            logger.Info("{0} health rule violations from {1:o} to {2:o}", listOfHealthRuleViolations.Count, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);
                            loggerConsole.Info("{0} health rule violations from {1:o} to {2:o}", listOfHealthRuleViolations.Count, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);
                        }

                        #endregion

                        #region Events

                        foreach (string eventType in eventTypes)
                        {
                            loggerConsole.Info("Extract {0} events ({1} time ranges)", eventType, jobConfiguration.Input.HourlyTimeRanges.Count);

                            JArray listOfEvents = new JArray();
                            int totalEventsFound = 0;
                            foreach (JobTimeRange jobTimeRange in jobConfiguration.Input.HourlyTimeRanges)
                            {
                                long fromTimeUnix = convertToUnixTimestamp(jobTimeRange.From);
                                long toTimeUnix = convertToUnixTimestamp(jobTimeRange.To);

                                string eventsJSON = controllerApi.GetEvents(jobTarget.ApplicationID, eventType, fromTimeUnix, toTimeUnix);
                                if (eventsJSON != String.Empty)
                                {
                                    Console.Write(".");

                                    // Load health rule violations
                                    JArray eventsInHour = JArray.Parse(eventsJSON);
                                    foreach (JObject interestingEvent in eventsInHour)
                                    {
                                        listOfEvents.Add(interestingEvent);
                                    }
                                    totalEventsFound = totalEventsFound + eventsInHour.Count;
                                    Console.Write("+{0}", eventsInHour.Count);
                                }
                            }

                            Console.WriteLine();

                            if (listOfEvents.Count > 0)
                            {
                                string eventsFilePath = Path.Combine(
                                    eventsFolderPath, 
                                    String.Format(EVENTS_FILE_NAME, eventType, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                                FileIOHelper.writeJArrayToFile(listOfEvents, eventsFilePath);

                                logger.Info("{0} events from {1:o} to {2:o}", listOfEvents.Count, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);
                                loggerConsole.Info("{0} events from {1:o} to {2:o}", listOfEvents.Count, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);
                            }
                        }

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        #endregion

        #region Indexing steps

        private static bool stepIndexControllersApplicationsAndEntities(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string configFolderPath = Path.Combine(applicationFolderPath, CONFIGURATION_FOLDER_NAME);

                        // Entity files
                        string applicationsFilePath = Path.Combine(controllerFolderPath, EXTRACT_ENTITY_APPLICATIONS_FILE_NAME);
                        string applicationConfigFilePath = Path.Combine(configFolderPath, EXTRACT_CONFIGURATION_APPLICATION_FILE_NAME);
                        string tiersFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_TIERS_FILE_NAME);
                        string nodesFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_NODES_FILE_NAME);
                        string businessTransactionsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string backendsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BACKENDS_FILE_NAME);
                        string backendsDetailFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_BACKENDS_DETAIL_FILE_NAME);
                        string serviceEndPointsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string serviceEndPointsDetailFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_SERVICE_ENDPOINTS_DETAIL_FILE_NAME);
                        string errorsFilePath = Path.Combine(entitiesFolderPath, EXTRACT_ENTITY_ERRORS_FILE_NAME);

                        // Report files
                        string controllersReportFilePath = Path.Combine(programOptions.OutputJobFolderPath, CONVERT_ENTITY_CONTROLLERS_FILE_NAME);
                        string controllerReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_CONTROLLER_FILE_NAME);
                        string applicationsReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_APPLICATIONS_FILE_NAME);
                        string applicationReportFilePath = Path.Combine(applicationFolderPath, CONVERT_ENTITY_APPLICATION_FILE_NAME);
                        string tiersReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_TIERS_FILE_NAME);
                        string nodesReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_NODES_FILE_NAME);
                        string backendsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BACKENDS_FILE_NAME);
                        string businessTransactionsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string serviceEndpointsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string errorsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_ERRORS_FILE_NAME);

                        #endregion

                        #region Controller

                        loggerConsole.Info("Index List of Controllers");

                        // Create this row 
                        EntityController controller = new EntityController();
                        controller.Controller = jobTarget.Controller;
                        controller.ControllerLink = String.Format(DEEPLINK_CONTROLLER, controller.Controller, DEEPLINK_TIMERANGE_LAST_15_MINUTES);
                        controller.UserName = jobTarget.UserName;

                        // Lookup number of applications
                        // Load JSON file from the file system in case we are continuing the step after stopping
                        List<AppDRESTApplication> applicationsRESTList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTApplication>(applicationsFilePath);
                        if (applicationsRESTList != null)
                        {
                            controller.NumApps = applicationsRESTList.Count;
                        }

                        // Lookup version
                        // Load the configuration.xml from the child to parse the version
                        XmlDocument configXml = FileIOHelper.loadXmlDocumentFromFile(applicationConfigFilePath);
                        if (configXml != null)
                        {
                            string controllerVersion = configXml.SelectSingleNode("application").Attributes["controller-version"].Value;
                            // The version is in 
                            // <application controller-version="004-002-005-001">
                            string[] controllerVersionArray = controllerVersion.Split('-');
                            int[] controllerVersionArrayNum = new int[controllerVersionArray.Length];
                            for (int j = 0; j < controllerVersionArray.Length; j++)
                            {
                                controllerVersionArrayNum[j] = Convert.ToInt32(controllerVersionArray[j]);
                            }
                            controllerVersion = String.Join(".", controllerVersionArrayNum);
                            controller.Version = controllerVersion;
                        }
                        else
                        {
                            controller.Version = "No config data";
                        }

                        // Output single controller report CSV
                        List<EntityController> controllerList = new List<EntityController>(1);
                        controllerList.Add(controller);
                        if (File.Exists(controllerReportFilePath) == false)
                        {
                            FileIOHelper.writeListToCSVFile(controllerList, new ControllerEntityReportMap(), controllerReportFilePath);
                        }

                        // Now append this controller to the list of all controllers
                        List<EntityController> controllersList = FileIOHelper.readListFromCSVFile<EntityController>(controllersReportFilePath, new ControllerEntityReportMap());
                        if (controllersList == null || controllersList.Count == 0)
                        {
                            // First time, let's output these rows
                            controllersList = controllerList;
                        }
                        else
                        {
                            EntityController controllerExisting = controllersList.Where(c => c.Controller == controller.Controller).FirstOrDefault();
                            if (controllerExisting == null)
                            {
                                controllersList.Add(controller);
                            }
                        }
                        controllersList = controllersList.OrderBy(o => o.Controller).ToList();
                        FileIOHelper.writeListToCSVFile(controllersList, new ControllerEntityReportMap(), controllersReportFilePath);

                        #endregion

                        #region Nodes

                        List<AppDRESTNode> nodesRESTList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTNode>(nodesFilePath);
                        List<EntityNode> nodesList = null;
                        if (nodesRESTList != null)
                        {
                            loggerConsole.Info("Index List of Nodes ({0} entities)", nodesRESTList.Count);

                            nodesList = new List<EntityNode>(nodesRESTList.Count);

                            foreach (AppDRESTNode node in nodesRESTList)
                            {
                                EntityNode nodeRow = new EntityNode();
                                nodeRow.NodeID = node.id;
                                nodeRow.AgentPresent = node.appAgentPresent;
                                nodeRow.AgentType = node.agentType;
                                nodeRow.AgentVersion = node.appAgentVersion;
                                nodeRow.ApplicationName = jobTarget.Application;
                                nodeRow.ApplicationID = jobTarget.ApplicationID;
                                nodeRow.Controller = jobTarget.Controller;
                                nodeRow.MachineAgentPresent = node.machineAgentPresent;
                                nodeRow.MachineAgentVersion = node.machineAgentVersion;
                                nodeRow.MachineID = node.machineId;
                                nodeRow.MachineName = node.machineName;
                                nodeRow.MachineOSType = node.machineOSType;
                                nodeRow.NodeName = node.name;
                                nodeRow.TierID = node.tierId;
                                nodeRow.TierName = node.tierName;
                                nodeRow.MachineType = node.type;
                                if (nodeRow.AgentVersion != String.Empty)
                                {
                                    // Java agent looks like that
                                    //Server Agent v4.2.3.2 GA #12153 r13c5eb6a7acbfea4d6da465a3ae47412715e26fa 59-4.2.3.next-build
                                    //Server Agent v3.7.16.0 GA #2014-02-26_21-19-08 raf61d5f54753290c983f95173e74e6865f6ad123 130-3.7.16
                                    //Server Agent v4.2.7.1 GA #13005 rc04adaef4741dbb8f2e7c206bdb2a6614046798a 11-4.2.7.next-analytics
                                    //Server Agent v4.0.6.0 GA #2015-05-11_20-44-33 r7cb8945756a0779766bf1b4c32e49a96da7b8cfe 10-4.0.6.next
                                    //Server Agent v3.8.3.0 GA #2014-06-06_17-06-05 r34b2744775df248f79ffb2da2b4515b1f629aeb5 7-3.8.3.next
                                    //Server Agent v3.9.3.0 GA #2014-09-23_22-14-15 r05918cd8a4a8a63504a34f0f1c85511e207049b3 20-3.9.3.next
                                    //Server Agent v4.1.7.1 GA #9949 ra4a2721d52322207b626e8d4c88855c846741b3d 18-4.1.7.next-build
                                    //Server Agent v3.7.11.1 GA #2013-10-23_17-07-44 r41149afdb8ce39025051c25382b1cf77e2a7fed0 21
                                    //Server Agent v4.1.8.5 GA #10236 r8eca32e4695e8f6a5902d34a66bfc12da1e12241 45-4.1.8.next-controller

                                    // Apache agent looks like this
                                    // Proxy v4.2.5.1 GA SHA-1:.ad6c804882f518b3350f422489866ea2008cd664 #13146 35-4.2.5.next-build

                                    Regex regexVersion = new Regex(@"(?i).*v(\d*\.\d*\.\d*\.\d*).*", RegexOptions.IgnoreCase);
                                    Match match = regexVersion.Match(nodeRow.AgentVersion);
                                    if (match != null)
                                    {
                                        if (match.Groups.Count > 1)
                                        {
                                            nodeRow.AgentVersionRaw = nodeRow.AgentVersion;
                                            nodeRow.AgentVersion = match.Groups[1].Value;
                                        }
                                    }
                                }
                                if (nodeRow.MachineAgentVersion != String.Empty)
                                {
                                    // Machine agent looks like that 
                                    //Machine Agent v4.2.3.2 GA Build Date 2016 - 07 - 11 10:26:01
                                    //Machine Agent v3.7.16.0 GA Build Date 2014 - 02 - 26 21:20:29
                                    //Machine Agent v4.2.3.2 GA Build Date 2016 - 07 - 11 10:17:54
                                    //Machine Agent v4.0.6.0 GA Build Date 2015 - 05 - 11 20:56:44
                                    //Machine Agent v3.8.3.0 GA Build Date 2014 - 06 - 06 17:09:13
                                    //Machine Agent v4.1.7.1 GA Build Date 2015 - 11 - 24 20:49:24

                                    Regex regexVersion = new Regex(@"(?i).*Machine Agent.*v(\d*\.\d*\.\d*\.\d*).*", RegexOptions.IgnoreCase);
                                    Match match = regexVersion.Match(nodeRow.MachineAgentVersion);
                                    if (match != null)
                                    {
                                        if (match.Groups.Count > 1)
                                        {
                                            nodeRow.MachineAgentVersionRaw = nodeRow.MachineAgentVersion;
                                            nodeRow.MachineAgentVersion = match.Groups[1].Value;
                                        }
                                    }
                                }

                                updateEntityWithDeeplinks(nodeRow);

                                nodesList.Add(nodeRow);
                            }

                            // Sort them
                            nodesList = nodesList.OrderBy(o => o.TierName).ThenBy(o => o.NodeName).ToList();

                            updateEntitiesWithReportDetailLinksNodes(programOptions, jobConfiguration, jobTarget, nodesList);

                            FileIOHelper.writeListToCSVFile(nodesList, new NodeEntityReportMap(), nodesReportFilePath);
                        }

                        #endregion

                        #region Backends

                        List<AppDRESTBackend> backendsRESTList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTBackend>(backendsFilePath);
                        List<AppDRESTTier> tiersRESTList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTTier>(tiersFilePath);
                        List<EntityBackend> backendsList = null;
                        if (backendsRESTList != null)
                        {
                            loggerConsole.Info("Index List of Backends ({0} entities)", backendsRESTList.Count);

                            backendsList = new List<EntityBackend>(backendsRESTList.Count);


                            JObject backendsDetailsContainer = FileIOHelper.loadJObjectFromFile(backendsDetailFilePath);
                            JArray backendsDetails = null;
                            if (backendsDetailsContainer != null)
                            {
                                backendsDetails = (JArray)backendsDetailsContainer["backendListEntries"];
                            }

                            foreach (AppDRESTBackend backend in backendsRESTList)
                            {
                                EntityBackend backendRow = new EntityBackend();
                                backendRow.ApplicationName = jobTarget.Application;
                                backendRow.ApplicationID = jobTarget.ApplicationID;
                                backendRow.BackendID = backend.id;
                                backendRow.BackendName = backend.name;
                                backendRow.BackendType = backend.exitPointType;
                                backendRow.Controller = jobTarget.Controller;
                                backendRow.NodeID = backend.applicationComponentNodeId;
                                if (backendRow.NodeID > 0)
                                {
                                    // Look it up
                                    AppDRESTNode node = nodesRESTList.Where<AppDRESTNode>(n => n.id == backendRow.NodeID).FirstOrDefault();
                                    if (node != null) backendRow.NodeName = node.name;
                                }
                                backendRow.NumProps = backend.properties.Count;
                                if (backend.properties.Count >= 1)
                                {
                                    backendRow.Prop1Name = backend.properties[0].name;
                                    backendRow.Prop1Value = backend.properties[0].value;
                                }
                                if (backend.properties.Count >= 2)
                                {
                                    backendRow.Prop2Name = backend.properties[1].name;
                                    backendRow.Prop2Value = backend.properties[1].value;
                                }
                                if (backend.properties.Count >= 3)
                                {
                                    backendRow.Prop3Name = backend.properties[2].name;
                                    backendRow.Prop3Value = backend.properties[2].value;
                                }
                                if (backend.properties.Count >= 4)
                                {
                                    backendRow.Prop4Name = backend.properties[3].name;
                                    backendRow.Prop4Value = backend.properties[3].value;
                                }
                                if (backend.properties.Count >= 5)
                                {
                                    backendRow.Prop5Name = backend.properties[4].name;
                                    backendRow.Prop5Value = backend.properties[4].value;
                                }
                                backendRow.TierID = backend.tierId;
                                if (backendRow.TierID > 0)
                                {
                                    // Look it up
                                    AppDRESTTier tier = tiersRESTList.Where<AppDRESTTier>(t => t.id == backendRow.TierID).FirstOrDefault();
                                    if (tier != null) backendRow.TierName = tier.name;
                                }

                                // Look up the type in the callInfo\metaInfo
                                JObject backendDetail = (JObject)backendsDetails.Where(b => (long)b["id"] == backendRow.BackendID).FirstOrDefault();
                                if (backendDetail != null)
                                {
                                    JArray metaInfoArray = (JArray)backendDetail["callInfo"]["metaInfo"];
                                    JToken metaInfoExitPoint = metaInfoArray.Where(m => m["name"].ToString() == "exit-point-type").FirstOrDefault();
                                    if (metaInfoExitPoint != null)
                                    {
                                        string betterBackendType = metaInfoExitPoint["value"].ToString();
                                        if (betterBackendType != backendRow.BackendType)
                                        {
                                            backendRow.BackendType = betterBackendType;
                                        }
                                    }
                                }

                                updateEntityWithDeeplinks(backendRow);

                                backendsList.Add(backendRow);
                            }
                            // Sort them
                            backendsList = backendsList.OrderBy(o => o.BackendType).ThenBy(o => o.BackendName).ToList();

                            updateEntitiesWithReportDetailLinksBackends(programOptions, jobConfiguration, jobTarget, backendsList);

                            FileIOHelper.writeListToCSVFile(backendsList, new BackendEntityReportMap(), backendsReportFilePath);
                        }

                        #endregion

                        #region Business Transactions

                        List<AppDRESTBusinessTransaction> businessTransactionsRESTList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTBusinessTransaction>(businessTransactionsFilePath);
                        List<EntityBusinessTransaction> businessTransactionList = null;
                        if (businessTransactionsRESTList != null)
                        {
                            loggerConsole.Info("Index List of Business Transactions ({0} entities)", businessTransactionsRESTList.Count);

                            businessTransactionList = new List<EntityBusinessTransaction>(businessTransactionsRESTList.Count);

                            foreach (AppDRESTBusinessTransaction businessTransaction in businessTransactionsRESTList)
                            {
                                EntityBusinessTransaction businessTransactionRow = new EntityBusinessTransaction();
                                businessTransactionRow.ApplicationID = jobTarget.ApplicationID;
                                businessTransactionRow.ApplicationName = jobTarget.Application;
                                businessTransactionRow.BTID = businessTransaction.id;
                                businessTransactionRow.BTName = businessTransaction.name;
                                if (businessTransactionRow.BTName == "_APPDYNAMICS_DEFAULT_TX_")
                                {
                                    businessTransactionRow.BTType = "OVERFLOW";
                                }
                                else
                                {
                                    businessTransactionRow.BTType = businessTransaction.entryPointType;
                                }
                                businessTransactionRow.Controller = jobTarget.Controller;
                                businessTransactionRow.TierID = businessTransaction.tierId;
                                businessTransactionRow.TierName = businessTransaction.tierName;

                                updateEntityWithDeeplinks(businessTransactionRow);

                                businessTransactionList.Add(businessTransactionRow);
                            }

                            // Sort them
                            businessTransactionList = businessTransactionList.OrderBy(o => o.TierName).ThenBy(o => o.BTName).ToList();

                            updateEntitiesWithReportDetailLinksBusinessTransactions(programOptions, jobConfiguration, jobTarget, businessTransactionList);

                            FileIOHelper.writeListToCSVFile(businessTransactionList, new BusinessTransactionEntityReportMap(), businessTransactionsReportFilePath);
                        }

                        #endregion

                        #region Service Endpoints

                        List<AppDRESTMetric> serviceEndpointsRESTList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(serviceEndPointsFilePath);
                        List<EntityServiceEndpoint> serviceEndpointsList = null;
                        if (serviceEndpointsRESTList != null)
                        {
                            loggerConsole.Info("Index List of Service Endpoints ({0} entities)", tiersRESTList.Count);

                            serviceEndpointsList = new List<EntityServiceEndpoint>(serviceEndpointsRESTList.Count);

                            JObject serviceEndpointsDetailsContainer = FileIOHelper.loadJObjectFromFile(serviceEndPointsDetailFilePath);
                            JArray serviceEndpointsDetails = null;
                            if (serviceEndpointsDetailsContainer != null)
                            {
                                serviceEndpointsDetails = (JArray)serviceEndpointsDetailsContainer["serviceEndpointListEntries"];
                            }

                            foreach (AppDRESTMetric serviceEndpoint in serviceEndpointsRESTList)
                            {
                                EntityServiceEndpoint serviceEndpointRow = new EntityServiceEndpoint();
                                serviceEndpointRow.ApplicationID = jobTarget.ApplicationID;
                                serviceEndpointRow.ApplicationName = jobTarget.Application;
                                serviceEndpointRow.Controller = jobTarget.Controller;

                                // metricName
                                // BTM|Application Diagnostic Data|SEP:4855|Calls per Minute
                                //                                     ^^^^
                                //                                     ID
                                serviceEndpointRow.SEPID = Convert.ToInt32(serviceEndpoint.metricName.Split('|')[2].Split(':')[1]);

                                // metricPath
                                // Service Endpoints|ECommerce-Services|/appdynamicspilot/rest|Calls per Minute
                                //                                      ^^^^^^^^^^^^^^^^^^^^^^
                                //                                      SEP Name
                                serviceEndpointRow.SEPName = serviceEndpoint.metricPath.Split('|')[2];

                                serviceEndpointRow.TierName = serviceEndpoint.metricPath.Split('|')[1];
                                if (tiersRESTList != null)
                                {
                                    // metricPath
                                    // Service Endpoints|ECommerce-Services|/appdynamicspilot/rest|Calls per Minute
                                    //                   ^^^^^^^^^^^^^^^^^^
                                    //                   Tier
                                    AppDRESTTier tierForThisEntity = tiersRESTList.Where(tier => tier.name == serviceEndpointRow.TierName).FirstOrDefault();
                                    if (tierForThisEntity != null)
                                    {
                                        serviceEndpointRow.TierID = tierForThisEntity.id;
                                    }
                                }

                                JObject serviceEndpointDetail = (JObject)serviceEndpointsDetails.Where(s => (long)s["id"] == serviceEndpointRow.SEPID).FirstOrDefault();
                                if (serviceEndpointDetail != null)
                                {
                                    serviceEndpointRow.SEPType = serviceEndpointDetail["type"].ToString();
                                }

                                updateEntityWithDeeplinks(serviceEndpointRow);

                                serviceEndpointsList.Add(serviceEndpointRow);
                            }

                            // Sort them
                            serviceEndpointsList = serviceEndpointsList.OrderBy(o => o.TierName).ThenBy(o => o.SEPName).ToList();

                            updateEntitiesWithReportDetailLinksServiceEndpoints(programOptions, jobConfiguration, jobTarget, serviceEndpointsList);

                            FileIOHelper.writeListToCSVFile(serviceEndpointsList, new ServiceEndpointEntityReportMap(), serviceEndpointsReportFilePath);
                        }

                        #endregion

                        #region Errors

                        List<AppDRESTMetric> errorsRESTList = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(errorsFilePath);
                        List<EntityError> errorList = null;
                        if (errorsRESTList != null)
                        {
                            loggerConsole.Info("Index List of Errors ({0} entities)", errorsRESTList.Count);

                            errorList = new List<EntityError>(errorsRESTList.Count);

                            foreach (AppDRESTMetric error in errorsRESTList)
                            {
                                EntityError errorRow = new EntityError();
                                errorRow.ApplicationID = jobTarget.ApplicationID;
                                errorRow.ApplicationName = jobTarget.Application;
                                errorRow.Controller = jobTarget.Controller;

                                // metricName
                                // BTM|Application Diagnostic Data|Error:11626|Errors per Minute
                                //                                       ^^^^^
                                //                                       ID
                                errorRow.ErrorID = Convert.ToInt32(error.metricName.Split('|')[2].Split(':')[1]);

                                // metricPath
                                // Errors|ECommerce-Services|CommunicationsException : EOFException|Errors per Minute
                                //                           ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                                //                           Error Name
                                errorRow.ErrorName = error.metricPath.Split('|')[2];

                                errorRow.ErrorType = "Error";
                                // Do some analysis of the error type based on their name
                                if (errorRow.ErrorName.IndexOf("exception", 0, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    errorRow.ErrorType = "Exception";
                                }
                                // For things like 
                                // CommunicationException : IOException : CommunicationException : SocketException
                                // ServletException : RollbackException : DatabaseException : SQLNestedException : NoSuchElementException
                                string[] errorTokens = errorRow.ErrorName.Split(':');
                                for (int j = 0; j < errorTokens.Length; j++)
                                {
                                    errorTokens[j] = errorTokens[j].Trim();
                                }
                                if (errorTokens.Length >= 1)
                                {
                                    errorRow.ErrorLevel1 = errorTokens[0];
                                }
                                if (errorTokens.Length >= 2)
                                {
                                    errorRow.ErrorLevel2 = errorTokens[1];
                                }
                                if (errorTokens.Length >= 3)
                                {
                                    errorRow.ErrorLevel3 = errorTokens[2];
                                }
                                if (errorTokens.Length >= 4)
                                {
                                    errorRow.ErrorLevel4 = errorTokens[3];
                                }
                                if (errorTokens.Length >= 5)
                                {
                                    errorRow.ErrorLevel5 = errorTokens[4];
                                }
                                errorRow.ErrorDepth = errorTokens.Length;

                                // Check if last thing is a 3 digit number, then cast it and see what comes out
                                if (errorTokens[errorTokens.Length - 1].Length == 3)
                                {
                                    int httpCode = -1;
                                    if (Int32.TryParse(errorTokens[errorTokens.Length - 1], out httpCode) == true)
                                    {
                                        // Hmm, likely to be a HTTP code
                                        errorRow.ErrorType = "HTTP";
                                        errorRow.HttpCode = httpCode;
                                    }
                                }

                                errorRow.TierName = error.metricPath.Split('|')[1];
                                if (tiersRESTList != null)
                                {
                                    // metricPath
                                    // Errors|ECommerce-Services|CommunicationsException : EOFException|Errors per Minute
                                    //        ^^^^^^^^^^^^^^^^^^
                                    //        Tier
                                    AppDRESTTier tierForThisEntity = tiersRESTList.Where(tier => tier.name == errorRow.TierName).FirstOrDefault();
                                    if (tierForThisEntity != null)
                                    {
                                        errorRow.TierID = tierForThisEntity.id;
                                    }
                                }

                                updateEntityWithDeeplinks(errorRow);

                                errorList.Add(errorRow);
                            }

                            // Sort them
                            errorList = errorList.OrderBy(o => o.TierName).ThenBy(o => o.ErrorName).ToList();

                            updateEntitiesWithReportDetailLinksErrors(programOptions, jobConfiguration, jobTarget, errorList);

                            FileIOHelper.writeListToCSVFile(errorList, new ErrorEntityReportMap(), errorsReportFilePath);
                        }

                        #endregion

                        #region Tiers

                        List<EntityTier> tiersList = null;
                        if (tiersRESTList != null)
                        {
                            loggerConsole.Info("Index List of Tiers ({0} entities)", tiersRESTList.Count);

                            tiersList = new List<EntityTier>(tiersRESTList.Count);

                            foreach (AppDRESTTier tier in tiersRESTList)
                            {
                                EntityTier tierRow = new EntityTier();
                                tierRow.AgentType = tier.agentType;
                                tierRow.ApplicationID = jobTarget.ApplicationID;
                                tierRow.ApplicationName = jobTarget.Application;
                                tierRow.Controller = jobTarget.Controller;
                                tierRow.TierID = tier.id;
                                tierRow.TierName = tier.name;
                                tierRow.TierType = tier.type;
                                tierRow.NumNodes = tier.numberOfNodes;
                                if (businessTransactionsRESTList != null)
                                {
                                    tierRow.NumBTs = businessTransactionsRESTList.Where<AppDRESTBusinessTransaction>(b => b.tierId == tierRow.TierID).Count();
                                }
                                if (serviceEndpointsList != null)
                                {
                                    tierRow.NumSEPs = serviceEndpointsList.Where<EntityServiceEndpoint>(s => s.TierID == tierRow.TierID).Count();
                                }
                                if (errorList != null)
                                {
                                    tierRow.NumErrors = errorList.Where<EntityError>(s => s.TierID == tierRow.TierID).Count();
                                }

                                updateEntityWithDeeplinks(tierRow);

                                tiersList.Add(tierRow);
                            }

                            // Sort them
                            tiersList = tiersList.OrderBy(o => o.TierName).ToList();

                            updateEntitiesWithReportDetailLinksTiers(programOptions, jobConfiguration, jobTarget, tiersList);

                            FileIOHelper.writeListToCSVFile(tiersList, new TierEntityReportMap(), tiersReportFilePath);
                        }

                        #endregion

                        #region Application

                        if (applicationsRESTList != null)
                        {
                            loggerConsole.Info("Index List of Applications");

                            List<EntityApplication> applicationsList = FileIOHelper.readListFromCSVFile<EntityApplication>(applicationsReportFilePath, new ApplicationEntityReportMap());

                            if (applicationsList == null || applicationsList.Count == 0)
                            {
                                // First time, let's output these rows
                                applicationsList = new List<EntityApplication>(applicationsRESTList.Count);
                                foreach (AppDRESTApplication application in applicationsRESTList)
                                {
                                    EntityApplication applicationsRow = new EntityApplication();
                                    applicationsRow.ApplicationName = application.name;
                                    applicationsRow.ApplicationID = application.id;
                                    applicationsRow.Controller = jobTarget.Controller;

                                    updateEntityWithDeeplinks(applicationsRow);

                                    applicationsList.Add(applicationsRow);
                                }
                            }

                            // Update counts of entities for this application row
                            EntityApplication applicationRow = applicationsList.Where(a => a.ApplicationID == jobTarget.ApplicationID).FirstOrDefault();
                            if (applicationRow != null)
                            {
                                if (tiersList != null) applicationRow.NumTiers = tiersList.Count;
                                if (nodesList != null) applicationRow.NumNodes = nodesList.Count;
                                if (backendsList != null) applicationRow.NumBackends = backendsList.Count;
                                if (businessTransactionList != null) applicationRow.NumBTs = businessTransactionList.Count;
                                if (serviceEndpointsList != null) applicationRow.NumSEPs = serviceEndpointsList.Count;
                                if (errorList != null) applicationRow.NumErrors = errorList.Count;

                                List<EntityApplication> applicationRows = new List<EntityApplication>(1);
                                applicationRows.Add(applicationRow);

                                // Write just this row for this application
                                FileIOHelper.writeListToCSVFile(applicationRows, new ApplicationEntityReportMap(), applicationReportFilePath);
                            }

                            // Sort them
                            applicationsList = applicationsList.OrderBy(o => o.Controller).ThenBy(o => o.ApplicationName).ToList();

                            updateEntitiesWithReportDetailLinksApplication(programOptions, jobConfiguration, jobTarget, applicationsList);

                            FileIOHelper.writeListToCSVFile(applicationsList, new ApplicationEntityReportMap(), applicationsReportFilePath);
                        }

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepIndexControllerAndApplicationConfiguration(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            loggerConsole.Fatal("TODO {0}({0:d})", jobStatus);
            return true;

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        // Business Transaction Rules

                        // Backend Rules configuration/backend-match-point-configurations

                        // Data Collectors

                        // Agent properties

                        // Health Rules configuration/health-rules

                        // Error Detection configuration/error-configuration

                        loggerConsole.Fatal("TODO {0}({0:d})", jobStatus);

                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepIndexApplicationAndEntityMetrics(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string metricsFolderPath = Path.Combine(applicationFolderPath, METRICS_FOLDER_NAME);

                        // Report files
                        string applicationReportFilePath = Path.Combine(applicationFolderPath, CONVERT_ENTITY_APPLICATION_FILE_NAME);
                        string tiersReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_TIERS_FILE_NAME);
                        string nodesReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_NODES_FILE_NAME);
                        string backendsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BACKENDS_FILE_NAME);
                        string businessTransactionsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string serviceEndpointsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string errorsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_ERRORS_FILE_NAME);

                        // Metric files
                        string metricsEntityFolderPath = String.Empty;
                        string metricsDataFilePath = String.Empty;
                        string entityFullRangeReportFilePath = String.Empty;
                        string entityHourlyRangeReportFilePath = String.Empty;
                        string entitiesFullRangeReportFilePath = String.Empty;
                        string entitiesHourlyRangeReportFilePath = String.Empty;

                        #endregion

                        #region Application

                        List<EntityApplication> applicationList = FileIOHelper.readListFromCSVFile<EntityApplication>(applicationReportFilePath, new ApplicationEntityReportMap());
                        if (applicationList != null && applicationList.Count > 0)
                        {
                            loggerConsole.Info("Index Metrics for Application ({0} entities)", applicationList.Count);

                            List<EntityApplication> applicationFullList = new List<EntityApplication>(1);
                            List<EntityApplication> applicationHourlyList = new List<EntityApplication>(jobConfiguration.Input.HourlyTimeRanges.Count);

                            metricsEntityFolderPath = Path.Combine(
                                metricsFolderPath,
                                APPLICATION_TYPE_SHORT);

                            #region Full Range

                            EntityApplication applicationRow = applicationList[0].Clone();
                            if (fillFullRangeMetricEntityRow(applicationRow, metricsEntityFolderPath, jobConfiguration.Input.ExpandedTimeRange) == true)
                            {
                                applicationFullList.Add(applicationRow);
                            }

                            #endregion

                            #region Hourly ranges

                            for (int k = 0; k < jobConfiguration.Input.HourlyTimeRanges.Count; k++)
                            {
                                JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[k];

                                applicationRow = applicationList[0].Clone();
                                if (fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(applicationRow, metricsEntityFolderPath, jobTimeRange, k) == true)
                                {
                                    applicationHourlyList.Add(applicationRow);
                                }
                            }

                            #endregion

                            updateEntitiesWithReportDetailLinksApplication(programOptions, jobConfiguration, jobTarget, applicationFullList);
                            updateEntitiesWithReportDetailLinksApplication(programOptions, jobConfiguration, jobTarget, applicationHourlyList);

                            entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(applicationFullList, new ApplicationMetricReportMap(), entityFullRangeReportFilePath);

                            entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(applicationHourlyList, new ApplicationMetricReportMap(), entityHourlyRangeReportFilePath);
                        }

                        #endregion

                        #region Tier

                        List<EntityTier> tiersList = FileIOHelper.readListFromCSVFile<EntityTier>(tiersReportFilePath, new TierEntityReportMap());
                        if (tiersList != null)
                        {
                            loggerConsole.Info("Index Metrics for Tiers ({0} entities)", tiersList.Count);

                            List<EntityTier> tiersFullList = new List<EntityTier>(tiersList.Count);
                            List<EntityTier> tiersHourlyList = new List<EntityTier>(tiersList.Count * jobConfiguration.Input.HourlyTimeRanges.Count);

                            int j = 0;

                            foreach (EntityTier tierRowOriginal in tiersList)
                            {
                                List<EntityTier> tierFullList = new List<EntityTier>(1);
                                List<EntityTier> tierHourlyList = new List<EntityTier>(jobConfiguration.Input.HourlyTimeRanges.Count);

                                metricsEntityFolderPath = Path.Combine(
                                    metricsFolderPath,
                                    TIERS_TYPE_SHORT,
                                    getShortenedEntityNameForFileSystem(tierRowOriginal.TierName, tierRowOriginal.TierID));

                                #region Full Range

                                EntityTier tierRow = tierRowOriginal.Clone();
                                if (fillFullRangeMetricEntityRow(tierRow, metricsEntityFolderPath, jobConfiguration.Input.ExpandedTimeRange) == true)
                                {
                                    tiersFullList.Add(tierRow);
                                    tierFullList.Add(tierRow);
                                }

                                #endregion

                                #region Hourly ranges

                                for (int k = 0; k < jobConfiguration.Input.HourlyTimeRanges.Count; k++)
                                {
                                    JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[k];

                                    tierRow = tierRowOriginal.Clone();
                                    if (fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(tierRow, metricsEntityFolderPath, jobTimeRange, k) == true)
                                    {
                                        tiersHourlyList.Add(tierRow);
                                        tierHourlyList.Add(tierRow);
                                    }
                                }

                                #endregion

                                entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(tierFullList, new TierMetricReportMap(), entityFullRangeReportFilePath);

                                entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(tierHourlyList, new TierMetricReportMap(), entityHourlyRangeReportFilePath);

                                j++;
                                if (j % 50 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);

                            // Sort them
                            tiersHourlyList = tiersHourlyList.OrderBy(o => o.TierName).ThenBy(o => o.From).ToList();

                            updateEntitiesWithReportDetailLinksTiers(programOptions, jobConfiguration, jobTarget, tiersFullList);
                            updateEntitiesWithReportDetailLinksTiers(programOptions, jobConfiguration, jobTarget, tiersHourlyList);

                            entityFullRangeReportFilePath = Path.Combine(metricsFolderPath, TIERS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(tiersFullList, new TierMetricReportMap(), entityFullRangeReportFilePath);

                            entityHourlyRangeReportFilePath = Path.Combine(metricsFolderPath, TIERS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(tiersHourlyList, new TierMetricReportMap(), entityHourlyRangeReportFilePath);
                        }

                        #endregion

                        #region Nodes

                        List<EntityNode> nodesList = FileIOHelper.readListFromCSVFile<EntityNode>(nodesReportFilePath, new NodeEntityReportMap());
                        if (nodesList != null)
                        {
                            loggerConsole.Info("Index Metrics for Nodes ({0} entities)", nodesList.Count);

                            List<EntityNode> nodesFullList = new List<EntityNode>(nodesList.Count);
                            List<EntityNode> nodesHourlyList = new List<EntityNode>(nodesList.Count * jobConfiguration.Input.HourlyTimeRanges.Count);

                            int j = 0;

                            foreach (EntityNode nodeRowOriginal in nodesList)
                            {
                                List<EntityNode> nodeFullList = new List<EntityNode>(1);
                                List<EntityNode> nodeHourlyList = new List<EntityNode>(jobConfiguration.Input.HourlyTimeRanges.Count);

                                metricsEntityFolderPath = Path.Combine(
                                    metricsFolderPath,
                                    NODES_TYPE_SHORT,
                                    getShortenedEntityNameForFileSystem(nodeRowOriginal.TierName, nodeRowOriginal.TierID),
                                    getShortenedEntityNameForFileSystem(nodeRowOriginal.NodeName, nodeRowOriginal.NodeID));

                                #region Full Range

                                EntityNode nodeRow = nodeRowOriginal.Clone();
                                if (fillFullRangeMetricEntityRow(nodeRow, metricsEntityFolderPath, jobConfiguration.Input.ExpandedTimeRange) == true)
                                {
                                    nodesFullList.Add(nodeRow);
                                    nodeFullList.Add(nodeRow);
                                }

                                #endregion

                                #region Hourly ranges

                                for (int k = 0; k < jobConfiguration.Input.HourlyTimeRanges.Count; k++)
                                {
                                    JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[k];

                                    nodeRow = nodeRowOriginal.Clone();
                                    if (fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(nodeRow, metricsEntityFolderPath, jobTimeRange, k) == true)
                                    {
                                        nodesHourlyList.Add(nodeRow);
                                        nodeHourlyList.Add(nodeRow);
                                    }
                                }

                                #endregion

                                entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(nodeFullList, new NodeMetricReportMap(), entityFullRangeReportFilePath);

                                entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(nodeHourlyList, new NodeMetricReportMap(), entityHourlyRangeReportFilePath);

                                j++;
                                if (j % 50 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);

                            // Sort them
                            nodesHourlyList = nodesHourlyList.OrderBy(o => o.TierName).ThenBy(o => o.NodeName).ThenBy(o => o.From).ToList();

                            updateEntitiesWithReportDetailLinksNodes(programOptions, jobConfiguration, jobTarget, nodesFullList);
                            updateEntitiesWithReportDetailLinksNodes(programOptions, jobConfiguration, jobTarget, nodesHourlyList);

                            entityFullRangeReportFilePath = Path.Combine(metricsFolderPath, NODES_TYPE_SHORT, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(nodesFullList, new NodeMetricReportMap(), entityFullRangeReportFilePath);

                            entityHourlyRangeReportFilePath = Path.Combine(metricsFolderPath, NODES_TYPE_SHORT, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(nodesHourlyList, new NodeMetricReportMap(), entityHourlyRangeReportFilePath);
                        }

                        #endregion

                        #region Backends

                        List<EntityBackend> backendsList = FileIOHelper.readListFromCSVFile<EntityBackend>(backendsReportFilePath, new BackendEntityReportMap());
                        if (backendsList != null)
                        {
                            loggerConsole.Info("Index Metrics for Backends ({0} entities)", backendsList.Count);

                            List<EntityBackend> backendsFullList = new List<EntityBackend>(backendsList.Count);
                            List<EntityBackend> backendsHourlyList = new List<EntityBackend>(backendsList.Count * jobConfiguration.Input.HourlyTimeRanges.Count);

                            int j = 0;

                            foreach (EntityBackend backendRowOriginal in backendsList)
                            {
                                List<EntityBackend> backendFullList = new List<EntityBackend>(1);
                                List<EntityBackend> backendHourlyList = new List<EntityBackend>(jobConfiguration.Input.HourlyTimeRanges.Count);

                                metricsEntityFolderPath = Path.Combine(
                                    metricsFolderPath,
                                    BACKENDS_TYPE_SHORT,
                                    getShortenedEntityNameForFileSystem(backendRowOriginal.BackendName, backendRowOriginal.BackendID));

                                #region Full Range
                                
                                EntityBackend backendRow = backendRowOriginal.Clone();
                                if (fillFullRangeMetricEntityRow(backendRow, metricsEntityFolderPath, jobConfiguration.Input.ExpandedTimeRange) == true)
                                {
                                    backendsFullList.Add(backendRow);
                                    backendFullList.Add(backendRow);
                                }

                                #endregion

                                #region Hourly ranges

                                for (int k = 0; k < jobConfiguration.Input.HourlyTimeRanges.Count; k++)
                                {
                                    JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[k];

                                    backendRow = backendRowOriginal.Clone();
                                    if (fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(backendRow, metricsEntityFolderPath, jobTimeRange, k) == true)
                                    {
                                        backendsHourlyList.Add(backendRow);
                                        backendHourlyList.Add(backendRow);
                                    }
                                }

                                #endregion

                                entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(backendFullList, new BackendMetricReportMap(), entityFullRangeReportFilePath);

                                entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(backendHourlyList, new BackendMetricReportMap(), entityHourlyRangeReportFilePath);

                                j++;
                                if (j % 10 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);

                            // Sort them
                            backendsHourlyList = backendsHourlyList.OrderBy(o => o.BackendType).ThenBy(o => o.BackendName).ThenBy(o => o.From).ToList();

                            updateEntitiesWithReportDetailLinksBackends(programOptions, jobConfiguration, jobTarget, backendsFullList);
                            updateEntitiesWithReportDetailLinksBackends(programOptions, jobConfiguration, jobTarget, backendsHourlyList);

                            entityFullRangeReportFilePath = Path.Combine(metricsFolderPath, BACKENDS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(backendsFullList, new BackendMetricReportMap(), entityFullRangeReportFilePath);

                            entityHourlyRangeReportFilePath = Path.Combine(metricsFolderPath, BACKENDS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(backendsHourlyList, new BackendMetricReportMap(), entityHourlyRangeReportFilePath);
                        }

                        #endregion

                        #region Business Transactions

                        List<EntityBusinessTransaction> businessTransactionsList = FileIOHelper.readListFromCSVFile<EntityBusinessTransaction>(businessTransactionsReportFilePath, new BusinessTransactionEntityReportMap());
                        if (businessTransactionsList != null)
                        {
                            loggerConsole.Info("Index Metrics for Business Transactions ({0} entities)", businessTransactionsList.Count);

                            List<EntityBusinessTransaction> businessTransactionsFullList = new List<EntityBusinessTransaction>(businessTransactionsList.Count);
                            List<EntityBusinessTransaction> businessTransactionsHourlyList = new List<EntityBusinessTransaction>(businessTransactionsList.Count * jobConfiguration.Input.HourlyTimeRanges.Count);

                            int j = 0;

                            foreach (EntityBusinessTransaction businessTransactionRowOriginal in businessTransactionsList)
                            {
                                List<EntityBusinessTransaction> businessTransactionFullList = new List<EntityBusinessTransaction>(1);
                                List<EntityBusinessTransaction> businessTransactionHourlyList = new List<EntityBusinessTransaction>(jobConfiguration.Input.HourlyTimeRanges.Count);

                                metricsEntityFolderPath = Path.Combine(
                                    metricsFolderPath,
                                    BUSINESS_TRANSACTIONS_TYPE_SHORT,
                                    getShortenedEntityNameForFileSystem(businessTransactionRowOriginal.TierName, businessTransactionRowOriginal.TierID),
                                    getShortenedEntityNameForFileSystem(businessTransactionRowOriginal.BTName, businessTransactionRowOriginal.BTID));

                                #region Full Range

                                EntityBusinessTransaction businessTransactionRow = businessTransactionRowOriginal.Clone();
                                if (fillFullRangeMetricEntityRow(businessTransactionRow, metricsEntityFolderPath, jobConfiguration.Input.ExpandedTimeRange) == true)
                                {
                                    businessTransactionsFullList.Add(businessTransactionRow);
                                    businessTransactionFullList.Add(businessTransactionRow);
                                }

                                #endregion

                                #region Hourly ranges

                                for (int k = 0; k < jobConfiguration.Input.HourlyTimeRanges.Count; k++)
                                {
                                    JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[k];

                                    businessTransactionRow = businessTransactionRowOriginal.Clone();
                                    if (fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(businessTransactionRow, metricsEntityFolderPath, jobTimeRange, k) == true)
                                    {
                                        businessTransactionsHourlyList.Add(businessTransactionRow);
                                        businessTransactionHourlyList.Add(businessTransactionRow);
                                    }
                                }

                                #endregion

                                entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(businessTransactionFullList, new BusinessTransactionMetricReportMap(), entityFullRangeReportFilePath);

                                entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(businessTransactionHourlyList, new BusinessTransactionMetricReportMap(), entityHourlyRangeReportFilePath);

                                j++;
                                if (j % 50 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);

                            // Sort them
                            businessTransactionsHourlyList = businessTransactionsHourlyList.OrderBy(o => o.TierName).ThenBy(o => o.BTName).ThenBy(o => o.From).ToList();

                            updateEntitiesWithReportDetailLinksBusinessTransactions(programOptions, jobConfiguration, jobTarget, businessTransactionsFullList);
                            updateEntitiesWithReportDetailLinksBusinessTransactions(programOptions, jobConfiguration, jobTarget, businessTransactionsHourlyList);

                            entityFullRangeReportFilePath = Path.Combine(metricsFolderPath, BUSINESS_TRANSACTIONS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(businessTransactionsFullList, new BusinessTransactionMetricReportMap(), entityFullRangeReportFilePath);

                            entityHourlyRangeReportFilePath = Path.Combine(metricsFolderPath, BUSINESS_TRANSACTIONS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(businessTransactionsHourlyList, new BusinessTransactionMetricReportMap(), entityHourlyRangeReportFilePath);
                        }

                        #endregion

                        #region Service Endpoints

                        List<EntityServiceEndpoint> serviceEndpointsList = FileIOHelper.readListFromCSVFile<EntityServiceEndpoint>(serviceEndpointsReportFilePath, new ServiceEndpointEntityReportMap());
                        if (serviceEndpointsList != null)
                        {
                            loggerConsole.Info("Index Metrics for Service Endpoints ({0} entities)", serviceEndpointsList.Count);

                            List<EntityServiceEndpoint> serviceEndpointsFullList = new List<EntityServiceEndpoint>(serviceEndpointsList.Count);
                            List<EntityServiceEndpoint> serviceEndpointsHourlyList = new List<EntityServiceEndpoint>(serviceEndpointsList.Count * jobConfiguration.Input.HourlyTimeRanges.Count);

                            int j = 0;

                            foreach (EntityServiceEndpoint serviceEndpointRowOriginal in serviceEndpointsList)
                            {
                                List<EntityServiceEndpoint> serviceEndpointFullList = new List<EntityServiceEndpoint>(1);
                                List<EntityServiceEndpoint> serviceEndpointHourlyList = new List<EntityServiceEndpoint>(jobConfiguration.Input.HourlyTimeRanges.Count);

                                metricsEntityFolderPath = Path.Combine(
                                    metricsFolderPath,
                                    SERVICE_ENDPOINTS_TYPE_SHORT,
                                    getShortenedEntityNameForFileSystem(serviceEndpointRowOriginal.TierName, serviceEndpointRowOriginal.TierID),
                                    getShortenedEntityNameForFileSystem(serviceEndpointRowOriginal.SEPName, serviceEndpointRowOriginal.SEPID));

                                #region Full Range

                                EntityServiceEndpoint serviceEndpointRow = serviceEndpointRowOriginal.Clone();
                                if (fillFullRangeMetricEntityRow(serviceEndpointRow, metricsEntityFolderPath, jobConfiguration.Input.ExpandedTimeRange) == true)
                                {
                                    serviceEndpointsFullList.Add(serviceEndpointRow);
                                    serviceEndpointFullList.Add(serviceEndpointRow);
                                }

                                #endregion

                                #region Hourly ranges

                                for (int k = 0; k < jobConfiguration.Input.HourlyTimeRanges.Count; k++)
                                {
                                    JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[k];

                                    serviceEndpointRow = serviceEndpointRowOriginal.Clone();
                                    if (fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(serviceEndpointRow, metricsEntityFolderPath, jobTimeRange, k) == true)
                                    {
                                        serviceEndpointsHourlyList.Add(serviceEndpointRow);
                                        serviceEndpointHourlyList.Add(serviceEndpointRow);
                                    }
                                }

                                #endregion

                                entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(serviceEndpointFullList, new ServiceEndpointMetricReportMap(), entityFullRangeReportFilePath);

                                entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(serviceEndpointHourlyList, new ServiceEndpointMetricReportMap(), entityHourlyRangeReportFilePath);

                                j++;
                                if (j % 50 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);

                            // Sort them
                            serviceEndpointsHourlyList = serviceEndpointsHourlyList.OrderBy(o => o.TierName).ThenBy(o => o.SEPName).ThenBy(o => o.From).ToList();

                            updateEntitiesWithReportDetailLinksServiceEndpoints(programOptions, jobConfiguration, jobTarget, serviceEndpointsFullList);
                            updateEntitiesWithReportDetailLinksServiceEndpoints(programOptions, jobConfiguration, jobTarget, serviceEndpointsHourlyList);

                            entityFullRangeReportFilePath = Path.Combine(metricsFolderPath, SERVICE_ENDPOINTS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(serviceEndpointsFullList, new ServiceEndpointMetricReportMap(), entityFullRangeReportFilePath);

                            entityHourlyRangeReportFilePath = Path.Combine(metricsFolderPath, SERVICE_ENDPOINTS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(serviceEndpointsHourlyList, new ServiceEndpointMetricReportMap(), entityHourlyRangeReportFilePath);
                        }

                        #endregion

                        #region Errors

                        List<EntityError> errorsList = FileIOHelper.readListFromCSVFile<EntityError>(errorsReportFilePath, new ErrorEntityReportMap());
                        if (errorsList != null)
                        {
                            loggerConsole.Info("Index Metrics for Errors ({0} entities)", errorsList.Count);

                            List<EntityError> errorsFullList = new List<EntityError>(errorsList.Count);
                            List<EntityError> errorsHourlyList = new List<EntityError>(errorsList.Count * jobConfiguration.Input.HourlyTimeRanges.Count);

                            int j = 0;

                            foreach (EntityError errorRowOriginal in errorsList)
                            {
                                List<EntityError> errorFullList = new List<EntityError>(1);
                                List<EntityError> errorHourlyList = new List<EntityError>(jobConfiguration.Input.HourlyTimeRanges.Count);

                                metricsEntityFolderPath = Path.Combine(
                                    metricsFolderPath,
                                    ERRORS_TYPE_SHORT,
                                    getShortenedEntityNameForFileSystem(errorRowOriginal.TierName, errorRowOriginal.TierID),
                                    getShortenedEntityNameForFileSystem(errorRowOriginal.ErrorName, errorRowOriginal.ErrorID));

                                #region Full Range

                                EntityError errorRow = errorRowOriginal.Clone();
                                if (fillFullRangeMetricEntityRow(errorRow, metricsEntityFolderPath, jobConfiguration.Input.ExpandedTimeRange) == true)
                                {
                                    errorsFullList.Add(errorRow);
                                    errorFullList.Add(errorRow);
                                }

                                #endregion

                                #region Hourly ranges

                                for (int k = 0; k < jobConfiguration.Input.HourlyTimeRanges.Count; k++)
                                {
                                    JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[k];

                                    errorRow = errorRowOriginal.Clone();
                                    if (fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(errorRow, metricsEntityFolderPath, jobTimeRange, k) == true)
                                    {
                                        errorsHourlyList.Add(errorRow);
                                        errorHourlyList.Add(errorRow);
                                    }
                                }

                                #endregion

                                entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(errorFullList, new ErrorMetricReportMap(), entityFullRangeReportFilePath);

                                entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                                FileIOHelper.writeListToCSVFile(errorHourlyList, new ErrorMetricReportMap(), entityHourlyRangeReportFilePath);

                                j++;
                                if (j % 50 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);

                            // Sort them
                            errorsHourlyList = errorsHourlyList.OrderBy(o => o.TierName).ThenBy(o => o.ErrorName).ThenBy(o => o.From).ToList();

                            updateEntitiesWithReportDetailLinksErrors(programOptions, jobConfiguration, jobTarget, errorsFullList);
                            updateEntitiesWithReportDetailLinksErrors(programOptions, jobConfiguration, jobTarget, errorsHourlyList);

                            entityFullRangeReportFilePath = Path.Combine(metricsFolderPath, ERRORS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(errorsFullList, new ErrorMetricReportMap(), entityFullRangeReportFilePath);

                            entityHourlyRangeReportFilePath = Path.Combine(metricsFolderPath, ERRORS_TYPE_SHORT, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                            FileIOHelper.writeListToCSVFile(errorsHourlyList, new ErrorMetricReportMap(), entityHourlyRangeReportFilePath);
                        }

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepIndexApplicationAndEntityFlowmaps(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string metricsFolderPath = Path.Combine(applicationFolderPath, METRICS_FOLDER_NAME);

                        // Report files
                        string applicationReportFilePath = Path.Combine(applicationFolderPath, CONVERT_ENTITY_APPLICATION_FILE_NAME);
                        string tiersReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_TIERS_FILE_NAME);
                        string nodesReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_NODES_FILE_NAME);
                        string backendsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BACKENDS_FILE_NAME);
                        string businessTransactionsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);

                        #endregion

                        #region Application

                        List<EntityApplication> applicationList = FileIOHelper.readListFromCSVFile<EntityApplication>(applicationReportFilePath, new ApplicationEntityReportMap());
                        if (applicationList != null && applicationList.Count > 0)
                        {
                            loggerConsole.Info("Index Flowmap for Application");

                            convertFlowmapApplication(programOptions, jobConfiguration, jobTarget, applicationList[0], metricsFolderPath);
                        }

                        #endregion

                        #region Tiers

                        List<EntityTier> tiersList = FileIOHelper.readListFromCSVFile<EntityTier>(tiersReportFilePath, new TierEntityReportMap());
                        if (tiersList != null)
                        {
                            loggerConsole.Info("Index Flowmap for Tiers ({0} entities)", tiersList.Count);

                            int j = 0;

                            foreach (EntityTier tierRow in tiersList)
                            {
                                convertFlowmapTier(programOptions, jobConfiguration, jobTarget, tierRow, metricsFolderPath);

                                j++;
                                if (j % 10 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Nodes

                        List<EntityNode> nodesList = FileIOHelper.readListFromCSVFile<EntityNode>(nodesReportFilePath, new NodeEntityReportMap());
                        if (nodesList != null)
                        {
                            loggerConsole.Info("Index Flowmap for Nodes ({0} entities)", tiersList.Count);

                            int j = 0;

                            foreach (EntityNode nodeRow in nodesList)
                            {
                                convertFlowmapNode(programOptions, jobConfiguration, jobTarget, nodeRow, metricsFolderPath);

                                j++;
                                if (j % 10 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Backends

                        List<EntityBackend> backendsList = FileIOHelper.readListFromCSVFile<EntityBackend>(backendsReportFilePath, new BackendEntityReportMap());
                        if (backendsList != null)
                        {
                            loggerConsole.Info("Index Flowmap for Backends ({0} entities)", backendsList.Count);

                            int j = 0;

                            foreach (EntityBackend backendRow in backendsList)
                            {
                                convertFlowmapBackend(programOptions, jobConfiguration, jobTarget, backendRow, metricsFolderPath);

                                j++;
                                if (j % 10 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Business Transactions

                        List<EntityBusinessTransaction> businessTransactionsList = FileIOHelper.readListFromCSVFile<EntityBusinessTransaction>(businessTransactionsReportFilePath, new BusinessTransactionEntityReportMap());
                        if (businessTransactionsList != null)
                        {
                            loggerConsole.Info("Index Flowmap for Business Transactions ({0} entities)", businessTransactionsList.Count);

                            int j = 0;

                            foreach (EntityBusinessTransaction businessTransactionRow in businessTransactionsList)
                            {
                                convertFlowmapsBusinessTransaction(programOptions, jobConfiguration, jobTarget, businessTransactionRow, metricsFolderPath);

                                j++;
                                if (j % 10 == 0)
                                {
                                    Console.Write("[{0}].", j);
                                }
                            }
                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepIndexSnapshots(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string snapshotsFolderPath = Path.Combine(applicationFolderPath, SNAPSHOTS_FOLDER_NAME);

                        // Report files
                        string tiersReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_TIERS_FILE_NAME);
                        string backendsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BACKENDS_FILE_NAME);
                        string serviceEndpointsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string errorsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_ERRORS_FILE_NAME);

                        string snapshotsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_FILE_NAME);
                        string segmentsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_FILE_NAME);
                        string callExitsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_EXIT_CALLS_FILE_NAME);
                        string serviceEndpointCallsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_SERVICE_ENDPOINTS_CALLS_FILE_NAME);
                        string detectedErrorsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_DETECTED_ERRORS_FILE_NAME);
                        string businessDataFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_BUSINESS_DATA_FILE_NAME);

                        #endregion

                        #region Index Snapshots

                        // Process each hour at a time
                        loggerConsole.Info("Index Snapshots");

                        List<EntityTier> tiersList = FileIOHelper.readListFromCSVFile<EntityTier>(tiersReportFilePath, new TierEntityReportMap());
                        List<EntityBackend> backendsList = FileIOHelper.readListFromCSVFile<EntityBackend>(backendsReportFilePath, new BackendEntityReportMap());
                        List<EntityServiceEndpoint> serviceEndpointsList = FileIOHelper.readListFromCSVFile<EntityServiceEndpoint>(serviceEndpointsReportFilePath, new ServiceEndpointEntityReportMap());
                        List<EntityError> errorsList = FileIOHelper.readListFromCSVFile<EntityError>(errorsReportFilePath, new ErrorEntityReportMap());

                        int j = 0;

                        foreach (JobTimeRange jobTimeRange in jobConfiguration.Input.HourlyTimeRanges)
                        {
                            string snapshotsListFilePath = Path.Combine(snapshotsFolderPath, String.Format(EXTRACT_SNAPSHOTS_FILE_NAME, jobTimeRange.From, jobTimeRange.To));
                            JArray listOfSnapshotsInHour = FileIOHelper.loadJArrayFromFile(snapshotsListFilePath);

                            if (listOfSnapshotsInHour != null && listOfSnapshotsInHour.Count > 0)
                            {
                                loggerConsole.Info("Index Snapshots {0:o} to {1:o} ({2} snapshots)", jobTimeRange.From, jobTimeRange.To, listOfSnapshotsInHour.Count);

                                if (programOptions.ProcessSequentially == false)
                                {
                                    var listOfSnapshotsInHourChunks = listOfSnapshotsInHour.BreakListIntoChunks(SNAPSHOTS_INDEX_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                    Parallel.ForEach<List<JToken>, int>(
                                        listOfSnapshotsInHourChunks,
                                        new ParallelOptions { MaxDegreeOfParallelism = SNAPSHOTS_INDEX_NUMBER_OF_THREADS },
                                        () => 0,
                                        (listOfSnapshotsInHourChunk, loop, subtotal) =>
                                        {
                                            subtotal += indexSnapshots(programOptions, jobConfiguration, jobTarget, jobTimeRange, listOfSnapshotsInHourChunk, tiersList, backendsList, serviceEndpointsList, errorsList, false);
                                            return subtotal;
                                        },
                                        (finalResult) =>
                                        {
                                            j = Interlocked.Add(ref j, finalResult);
                                            Console.Write("[{0}].", j);
                                        }
                                    );
                                }
                                else
                                {
                                    j = indexSnapshots(programOptions, jobConfiguration, jobTarget, jobTimeRange, listOfSnapshotsInHour.ToList<JToken>(), tiersList, backendsList, serviceEndpointsList, errorsList, true);
                                }

                                loggerConsole.Info("{0} snapshots", j);
                            }
                        }

                        #endregion

                        #region Combine Snapshots, Segments, Call Exits, Service Endpoints and Business Data

                        // Assemble snapshot files into summary file for entire application

                        loggerConsole.Info("Combine Snapshots");

                        j = 0;

                        List<Snapshot> allSnapshotList = new List<Snapshot>();
                        List<Segment> allSegmentList = new List<Segment>();
                        List<ExitCall> allExitCallsList = new List<ExitCall>();
                        List<ServiceEndpointCall> allServiceEndpointCallsList = new List<ServiceEndpointCall>();
                        List<DetectedError> allDetectedErrorsList = new List<DetectedError>();
                        List<BusinessData> allBusinessDataList = new List<BusinessData>();

                        foreach (JobTimeRange jobTimeRange in jobConfiguration.Input.HourlyTimeRanges)
                        {
                            string snapshotsListFilePath = Path.Combine(snapshotsFolderPath, String.Format(EXTRACT_SNAPSHOTS_FILE_NAME, jobTimeRange.From, jobTimeRange.To));
                            JArray listOfSnapshotsInHour = FileIOHelper.loadJArrayFromFile(snapshotsListFilePath);

                            if (listOfSnapshotsInHour != null && listOfSnapshotsInHour.Count > 0)
                            {
                                foreach (JToken snapshot in listOfSnapshotsInHour)
                                {
                                    DateTime snapshotTime = convertFromUnixTimestamp((long)snapshot["serverStartTime"]);

                                    string snapshotFolderPath = Path.Combine(
                                    snapshotsFolderPath,
                                    getShortenedEntityNameForFileSystem(snapshot["applicationComponentName"].ToString(), (long)snapshot["applicationComponentId"]),
                                    getShortenedEntityNameForFileSystem(snapshot["businessTransactionName"].ToString(), (long)snapshot["businessTransactionId"]),
                                    String.Format("{0:yyyyMMddHH}", snapshotTime),
                                    userExperienceFolderNameMapping[snapshot["userExperience"].ToString()],
                                    String.Format(SNAPSHOT_FOLDER_NAME, snapshot["requestGUID"], snapshotTime));

                                    string thisSnapshotSnapshotsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_FILE_NAME);
                                    string thisSnapshotSegmentsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_FILE_NAME);
                                    string thisSnapshotExitCallsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_EXIT_CALLS_FILE_NAME);
                                    string thisSnapshotServiceEndpointCallsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_SERVICE_ENDPOINT_CALLS_FILE_NAME);
                                    string thisSnapshotDetectedErrorsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_DETECTED_ERRORS_FILE_NAME);
                                    string thisSnapshotBusinessDataFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_BUSINESS_DATA_FILE_NAME);

                                    List<Snapshot> thisSnapshotSnapshotList = FileIOHelper.readListFromCSVFile<Snapshot>(thisSnapshotSnapshotsFileName, new SnapshotReportMap());
                                    List<Segment> thisSnapshotSegmentList = FileIOHelper.readListFromCSVFile<Segment>(thisSnapshotSegmentsFileName, new SegmentReportMap());
                                    List<ExitCall> thisSnapshotExitCallList = FileIOHelper.readListFromCSVFile<ExitCall>(thisSnapshotExitCallsFileName, new ExitCallReportMap());
                                    List<ServiceEndpointCall> thisServiceEndpointCallsList = FileIOHelper.readListFromCSVFile<ServiceEndpointCall>(thisSnapshotServiceEndpointCallsFileName, new ServiceEndpointCallReportMap());
                                    List<DetectedError> thisDetectedErrorsList = FileIOHelper.readListFromCSVFile<DetectedError>(thisSnapshotDetectedErrorsFileName, new DetectedErrorReportMap());
                                    List<BusinessData> thisBusinessDataList = FileIOHelper.readListFromCSVFile<BusinessData>(thisSnapshotBusinessDataFileName, new BusinessDataReportMap());

                                    if (thisSnapshotSnapshotList != null)
                                    {
                                        allSnapshotList.AddRange(thisSnapshotSnapshotList);
                                    }
                                    if (thisSnapshotSegmentList != null)
                                    {
                                        allSegmentList.AddRange(thisSnapshotSegmentList);
                                    }
                                    if (thisSnapshotExitCallList != null)
                                    {
                                        allExitCallsList.AddRange(thisSnapshotExitCallList);
                                    }
                                    if (thisServiceEndpointCallsList != null)
                                    {
                                        allServiceEndpointCallsList.AddRange(thisServiceEndpointCallsList);
                                    }
                                    if (thisDetectedErrorsList != null)
                                    {
                                        allDetectedErrorsList.AddRange(thisDetectedErrorsList);
                                    }
                                    if (thisBusinessDataList != null)
                                    {
                                        allBusinessDataList.AddRange(thisBusinessDataList);
                                    }

                                    j++;
                                    if (j % 100 == 0)
                                    {
                                        Console.Write("[{0}].", j);
                                    }
                                }
                            }
                        }
                        loggerConsole.Info("{0} snapshots", j);

                        // Note that occasionally the controller returns the same snapshot twice in the list. We process it into multiple records. 
                        // I could filter them here but I don't want to bother
                        allSnapshotList = allSnapshotList.OrderBy(s => s.Occured).ThenBy(s => s.TierID).ThenBy(s => s.BTID).ThenBy(s => s.UserExperience).ToList();
                        allSegmentList = allSegmentList.OrderBy(s => s.Occured).ThenBy(s => s.TierID).ThenBy(s => s.BTID).ThenBy(s => s.UserExperience).ThenBy(s => s.RequestID).ToList();
                        allExitCallsList = allExitCallsList.OrderBy(e => e.TierID).ThenBy(e => e.BTName).ThenBy(e => e.RequestID).ThenBy(e => e.SegmentID).ThenBy(e => e.ExitType).ToList();
                        allServiceEndpointCallsList = allServiceEndpointCallsList.OrderBy(s => s.TierID).ThenBy(s => s.BTID).ThenBy(s => s.RequestID).ThenBy(s => s.SegmentID).ThenBy(s => s.SEPName).ToList();
                        allDetectedErrorsList = allDetectedErrorsList.OrderBy(e => e.TierID).ThenBy(e => e.BTID).ThenBy(e => e.RequestID).ThenBy(e => e.SegmentID).ThenBy(e => e.ErrorName).ToList();
                        allBusinessDataList = allBusinessDataList.OrderBy(b => b.TierID).ThenBy(b => b.BTID).ThenBy(b => b.RequestID).ThenBy(b => b.SegmentID).ThenBy(b => b.DataType).ThenBy(b => b.DataName).ToList();

                        // Save final results
                        FileIOHelper.writeListToCSVFile(allSnapshotList, new SnapshotReportMap(), snapshotsFilePath);
                        FileIOHelper.writeListToCSVFile(allSegmentList, new SegmentReportMap(), segmentsFilePath);
                        FileIOHelper.writeListToCSVFile(allExitCallsList, new ExitCallReportMap(), callExitsFilePath);
                        FileIOHelper.writeListToCSVFile(allServiceEndpointCallsList, new ServiceEndpointCallReportMap(), serviceEndpointCallsFilePath);
                        FileIOHelper.writeListToCSVFile(allDetectedErrorsList, new DetectedErrorReportMap(), detectedErrorsFilePath);
                        FileIOHelper.writeListToCSVFile(allBusinessDataList, new BusinessDataReportMap(), businessDataFilePath);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepIndexEvents(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Set up controller access
                        ControllerApi controllerApi = new ControllerApi(jobTarget.Controller, jobTarget.UserName, AESEncryptionHelper.Decrypt(jobTarget.UserPassword));

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        string eventsFolderPath = Path.Combine(applicationFolderPath, EVENTS_FOLDER_NAME);

                        #endregion

                        #region Health Rule violations

                        loggerConsole.Info("Index health rule violations");

                        List<HealthRuleViolationEvent> healthRuleViolationList = new List<HealthRuleViolationEvent>();

                        long fromTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.From);
                        long toTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.To);
                        long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
                        string DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);

                        string healthRuleViolationsDataFilePath = Path.Combine(
                            eventsFolderPath,
                            String.Format(HEALTH_RULE_VIOLATIONS_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                        if (File.Exists(healthRuleViolationsDataFilePath))
                        {
                            JArray eventsInHour = FileIOHelper.loadJArrayFromFile(healthRuleViolationsDataFilePath);
                            if (eventsInHour != null)
                            {
                                foreach (JObject interestingEvent in eventsInHour)
                                {
                                    HealthRuleViolationEvent eventRow = new HealthRuleViolationEvent();
                                    eventRow.Controller = jobTarget.Controller;
                                    eventRow.ApplicationName = jobTarget.Application;
                                    eventRow.ApplicationID = jobTarget.ApplicationID;

                                    eventRow.EventID = (long)interestingEvent["id"];
                                    eventRow.FromUtc = convertFromUnixTimestamp((long)interestingEvent["startTimeInMillis"]);
                                    eventRow.From = eventRow.FromUtc.ToLocalTime();
                                    if ((long)interestingEvent["endTimeInMillis"] > 0)
                                    {
                                        eventRow.ToUtc = convertFromUnixTimestamp((long)interestingEvent["endTimeInMillis"]);
                                        eventRow.To = eventRow.FromUtc.ToLocalTime();
                                    }
                                    eventRow.Status = interestingEvent["incidentStatus"].ToString();
                                    eventRow.Severity = interestingEvent["severity"].ToString();
                                    //eventRow.EventLink = interestingEvent["deepLinkUrl"].ToString();
                                    eventRow.EventLink = String.Format(DEEPLINK_INCIDENT, eventRow.Controller, eventRow.ApplicationID, eventRow.EventID, interestingEvent["startTimeInMillis"], DEEPLINK_THIS_TIMERANGE); ;
                                    
                                    eventRow.Description = interestingEvent["description"].ToString();

                                    if (interestingEvent["triggeredEntityDefinition"].HasValues == true)
                                    {
                                        eventRow.HealthRuleID = (int)interestingEvent["triggeredEntityDefinition"]["entityId"];
                                        eventRow.HealthRuleName = interestingEvent["triggeredEntityDefinition"]["name"].ToString();
                                        // TODO the health rule can't be hotlinked to until platform rewrites this nonsense from Flash
                                        eventRow.HealthRuleLink = String.Format(DEEPLINK_HEALTH_RULE, eventRow.Controller, eventRow.ApplicationID, eventRow.HealthRuleID, DEEPLINK_THIS_TIMERANGE);
                                    }

                                    if (interestingEvent["affectedEntityDefinition"].HasValues == true)
                                    {
                                        eventRow.EntityID = (int)interestingEvent["affectedEntityDefinition"]["entityId"];
                                        eventRow.EntityName = interestingEvent["affectedEntityDefinition"]["name"].ToString();

                                        string entityType = interestingEvent["affectedEntityDefinition"]["entityType"].ToString();
                                        if (entityTypeStringMapping.ContainsKey(entityType) == true)
                                        {
                                            eventRow.EntityType = entityTypeStringMapping[entityType];
                                        }
                                        else
                                        {
                                            eventRow.EntityType = entityType;
                                        }

                                        // Come up with links
                                        switch (entityType)
                                        {
                                            case ENTITY_TYPE_APPLICATION:
                                                eventRow.EntityLink = String.Format(DEEPLINK_APPLICATION, eventRow.Controller, eventRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                                                break;

                                            case ENTITY_TYPE_APPLICATION_MOBILE:
                                                eventRow.EntityLink = String.Format(DEEPLINK_APPLICATION_MOBILE, eventRow.Controller, eventRow.ApplicationID, eventRow.EntityID, DEEPLINK_THIS_TIMERANGE);
                                                break;

                                            case ENTITY_TYPE_TIER:
                                                eventRow.EntityLink = String.Format(DEEPLINK_TIER, eventRow.Controller, eventRow.ApplicationID, eventRow.EntityID, DEEPLINK_THIS_TIMERANGE);
                                                break;

                                            case ENTITY_TYPE_NODE:
                                                eventRow.EntityLink = String.Format(DEEPLINK_NODE, eventRow.Controller, eventRow.ApplicationID, eventRow.EntityID, DEEPLINK_THIS_TIMERANGE);
                                                break;

                                            case ENTITY_TYPE_BUSINESS_TRANSACTION:
                                                eventRow.EntityLink = String.Format(DEEPLINK_BUSINESS_TRANSACTION, eventRow.Controller, eventRow.ApplicationID, eventRow.EntityID, DEEPLINK_THIS_TIMERANGE);
                                                break;

                                            case ENTITY_TYPE_BACKEND:
                                                eventRow.EntityLink = String.Format(DEEPLINK_BACKEND, eventRow.Controller, eventRow.ApplicationID, eventRow.EntityID, DEEPLINK_THIS_TIMERANGE);
                                                break;

                                            default:
                                                logger.Warn("Unknown entity type {0} in affectedEntityDefinition in health rule violations", entityType);
                                                break;
                                        }
                                    }

                                    eventRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, eventRow.Controller, DEEPLINK_THIS_TIMERANGE);
                                    eventRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, eventRow.Controller, eventRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);

                                    healthRuleViolationList.Add(eventRow);
                                }
                            }
                        }

                        loggerConsole.Info("{0} events", healthRuleViolationList.Count);

                        // Sort them
                        healthRuleViolationList = healthRuleViolationList.OrderBy(o => o.HealthRuleName).ThenBy(o => o.From).ThenBy(o => o.Severity).ToList();

                        string healthRuleViolationEventsFilePath = Path.Combine(eventsFolderPath, CONVERT_HEALTH_RULE_EVENTS_FILE_NAME);
                        FileIOHelper.writeListToCSVFile<HealthRuleViolationEvent>(healthRuleViolationList, new HealthRuleViolationEventReportMap(), healthRuleViolationEventsFilePath);

                        #endregion

                        #region Events

                        loggerConsole.Info("Index events");

                        List<Event> eventsList = new List<Event>();
                        foreach (string eventType in eventTypes)
                        {
                            loggerConsole.Info("Type {0} events", eventType);

                            string eventsDataFilePath = Path.Combine(
                                eventsFolderPath, 
                                String.Format(EVENTS_FILE_NAME, eventType, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                            if (File.Exists(eventsDataFilePath))
                            {
                                JArray eventsInHour = FileIOHelper.loadJArrayFromFile(eventsDataFilePath);
                                if (eventsInHour != null)
                                {
                                    foreach (JObject interestingEvent in eventsInHour)
                                    {
                                        Event eventRow = new Event();
                                        eventRow.Controller = jobTarget.Controller;
                                        eventRow.ApplicationName = jobTarget.Application;
                                        eventRow.ApplicationID = jobTarget.ApplicationID;

                                        eventRow.EventID = (long)interestingEvent["id"];
                                        eventRow.OccuredUtc = convertFromUnixTimestamp((long)interestingEvent["eventTime"]);
                                        eventRow.Occured = eventRow.OccuredUtc.ToLocalTime();
                                        eventRow.Type = interestingEvent["type"].ToString();
                                        eventRow.SubType = interestingEvent["subType"].ToString();
                                        eventRow.Severity = interestingEvent["severity"].ToString();
                                        eventRow.EventLink = interestingEvent["deepLinkUrl"].ToString();
                                        eventRow.Summary = interestingEvent["summary"].ToString();

                                        if (interestingEvent["triggeredEntity"].HasValues == true)
                                        {
                                            eventRow.TriggeredEntityID = (int)interestingEvent["triggeredEntity"]["entityId"];
                                            eventRow.TriggeredEntityName = interestingEvent["triggeredEntity"]["name"].ToString();
                                            string entityType = interestingEvent["triggeredEntity"]["entityType"].ToString();
                                            if (entityTypeStringMapping.ContainsKey(entityType) == true)
                                            {
                                                eventRow.TriggeredEntityType = entityTypeStringMapping[entityType];
                                            }
                                            else
                                            {
                                                eventRow.TriggeredEntityType = entityType;
                                            }
                                        }

                                        foreach (JObject affectedEntity in interestingEvent["affectedEntities"])
                                        {
                                            string entityType = affectedEntity["entityType"].ToString();
                                            switch (entityType)
                                            {
                                                case ENTITY_TYPE_APPLICATION:
                                                    // already have this data
                                                    break;

                                                case ENTITY_TYPE_TIER:
                                                    eventRow.TierID = (int)affectedEntity["entityId"];
                                                    eventRow.TierName = affectedEntity["name"].ToString();
                                                    break;

                                                case ENTITY_TYPE_NODE:
                                                    eventRow.NodeID = (int)affectedEntity["entityId"];
                                                    eventRow.NodeName = affectedEntity["name"].ToString();
                                                    break;

                                                case ENTITY_TYPE_MACHINE:
                                                    eventRow.MachineID = (int)affectedEntity["entityId"];
                                                    eventRow.MachineName = affectedEntity["name"].ToString();
                                                    break;

                                                case ENTITY_TYPE_BUSINESS_TRANSACTION:
                                                    eventRow.BTID = (int)affectedEntity["entityId"];
                                                    eventRow.BTName = affectedEntity["name"].ToString();
                                                    break;

                                                case ENTITY_TYPE_HEALTH_RULE:
                                                    eventRow.TriggeredEntityID = (int)affectedEntity["entityId"];
                                                    eventRow.TriggeredEntityType = entityTypeStringMapping[affectedEntity["entityType"].ToString()];
                                                    eventRow.TriggeredEntityName = affectedEntity["name"].ToString();
                                                    break;

                                                default:
                                                    logger.Warn("Unknown entity type {0} in affectedEntities in events", entityType);
                                                    break;
                                            }
                                        }

                                        eventRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, eventRow.Controller, DEEPLINK_THIS_TIMERANGE);
                                        eventRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, eventRow.Controller, eventRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                                        if (eventRow.TierID != 0)
                                        {
                                            eventRow.TierLink = String.Format(DEEPLINK_TIER, eventRow.Controller, eventRow.ApplicationID, eventRow.TierID, DEEPLINK_THIS_TIMERANGE);
                                        }
                                        if (eventRow.NodeID != 0)
                                        {
                                            eventRow.NodeLink = String.Format(DEEPLINK_NODE, eventRow.Controller, eventRow.ApplicationID, eventRow.NodeID, DEEPLINK_THIS_TIMERANGE);
                                        }
                                        if (eventRow.BTID != 0)
                                        {
                                            eventRow.BTLink = String.Format(DEEPLINK_BUSINESS_TRANSACTION, eventRow.Controller, eventRow.ApplicationID, eventRow.BTID, DEEPLINK_THIS_TIMERANGE);
                                        }

                                        eventsList.Add(eventRow);
                                    }
                                }
                            }
                        }
                        loggerConsole.Info("{0} events", eventsList.Count);

                        // Sort them
                        eventsList = eventsList.OrderBy(o => o.Type).ThenBy(o => o.Occured).ThenBy(o => o.Severity).ToList();

                        string eventsFilePath = Path.Combine(eventsFolderPath, CONVERT_EVENTS_FILE_NAME);
                        FileIOHelper.writeListToCSVFile<Event>(eventsList, new EventReportMap(), eventsFilePath);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        #endregion

        #region Reporting steps

        private static bool stepReportControlerApplicationsAndEntities(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                loggerConsole.Info("Prepare Detected Entities Report File");

                #region Prepare the report package

                // Prepare package
                ExcelPackage excelDetectedEntities = new ExcelPackage();
                excelDetectedEntities.Workbook.Properties.Author = String.Format("AppDynamics DEXTER {0}", Assembly.GetEntryAssembly().GetName().Version);
                excelDetectedEntities.Workbook.Properties.Title = "AppDynamics DEXTER Detected Entities Report";
                excelDetectedEntities.Workbook.Properties.Subject = programOptions.JobName;

                excelDetectedEntities.Workbook.Properties.Comments = String.Format("Targets={0}\nFrom={1:o}\nTo={2:o}", jobConfiguration.Target.Count, jobConfiguration.Input.TimeRange.From, jobConfiguration.Input.TimeRange.To);

                #endregion

                #region Parameters sheet

                // Parameters sheet
                ExcelWorksheet sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_SHEET_PARAMETERS);

                var hyperLinkStyle = sheet.Workbook.Styles.CreateNamedStyle("HyperLinkStyle");
                hyperLinkStyle.Style.Font.UnderLineType = ExcelUnderLineType.Single;
                hyperLinkStyle.Style.Font.Color.SetColor(Color.Blue);

                int l = 1;
                sheet.Cells[l, 1].Value = "Table of Contents";
                sheet.Cells[l, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                l++; l++;
                sheet.Cells[l, 1].Value = "AppDynamics DEXTER Detected Entities Report";
                l++; l++;
                sheet.Cells[l, 1].Value = "From";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "To";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Number of Hours Intervals";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.HourlyTimeRanges.Count;
                l++;
                sheet.Cells[l, 1].Value = "Export Metrics";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Metrics;
                l++;
                sheet.Cells[l, 1].Value = "Export Snapshots";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Snapshots;
                l++;
                sheet.Cells[l, 1].Value = "Export Flowmaps";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Flowmaps;
                l++;
                sheet.Cells[l, 1].Value = "Export Configuration";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Configuration;
                l++;
                sheet.Cells[l, 1].Value = "Export Events";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Events;
                l++;
                sheet.Cells[l, 1].Value = "Targets:";
                l++; l++;
                ExcelRangeBase range = sheet.Cells[l, 1].LoadFromCollection(from jobTarget in jobConfiguration.Target
                                                                            select new
                                                                            {
                                                                                Controller = jobTarget.Controller,
                                                                                UserName = jobTarget.UserName,
                                                                                Application = jobTarget.Application,
                                                                                ApplicationID = jobTarget.ApplicationID,
                                                                                Status = jobTarget.Status.ToString()
                                                                            }, true);
                ExcelTable table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_PARAMETERS_TARGETS);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(1).AutoFit();
                sheet.Column(2).AutoFit();
                sheet.Column(3).AutoFit();

                #endregion

                #region TOC sheet

                // Navigation sheet with link to other sheets
                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_SHEET_TOC);

                #endregion

                #region Entity sheets and their associated pivots

                // Entity sheets
                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_CONTROLLERS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_APPLICATIONS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_TIERS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_TIERS_PIVOT);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_TIERS_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_TIERS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 4);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_NODES_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "Types of App Agent";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_APPAGENT_PIVOT);
                sheet.Cells[3, 1].Value = "Types of Machine Agent";
                sheet.Cells[3, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_MACHINEAGENT_PIVOT);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_APPAGENT_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_NODES_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 3, 5);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_MACHINEAGENT_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_NODES_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 5);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "Types of Backends";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_TYPE_PIVOT);
                sheet.Cells[3, 1].Value = "Locations of Backends";
                sheet.Cells[3, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LOCATION_PIVOT);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_TYPE_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 4);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LOCATION_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 5);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "Types of BTs";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_TYPE_PIVOT);
                sheet.Cells[3, 1].Value = "Location of BTs";
                sheet.Cells[3, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LOCATION_PIVOT);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_TYPE_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 5);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LOCATION_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "Type of SEPs";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_TYPE_PIVOT);
                sheet.Cells[3, 1].Value = "Location of SEPs";
                sheet.Cells[3, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LOCATION_PIVOT);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_TYPE_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 5);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LOCATION_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "Errors by Type";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_ERRORS_TYPE_PIVOT);
                sheet.Cells[3, 1].Value = "Location of Errors";
                sheet.Cells[3, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LOCATION_PIVOT_LOCATION);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_ERRORS_TYPE_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LIST);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 5);

                sheet = excelDetectedEntities.Workbook.Worksheets.Add(REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LOCATION_PIVOT_LOCATION);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LOCATION_PIVOT_LOCATION);
                sheet.View.FreezePanes(REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                #endregion

                List<string> listOfControllersAlreadyProcessed = new List<string>(jobConfiguration.Target.Count);

                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);

                        // Report files
                        string controllerReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_CONTROLLER_FILE_NAME);
                        string applicationsReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_APPLICATIONS_FILE_NAME);
                        string tiersReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_TIERS_FILE_NAME);
                        string nodesReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_NODES_FILE_NAME);
                        string backendsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BACKENDS_FILE_NAME);
                        string businessTransactionsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string serviceEndpointsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string errorsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_ERRORS_FILE_NAME);

                        // Sheet row counters
                        int numRowsToSkipInCSVFile = 0;
                        int fromRow = 1;

                        #endregion

                        #region Controllers and Applications

                        // Only output this once per controller
                        if (listOfControllersAlreadyProcessed.Contains(jobTarget.Controller) == false)
                        {
                            listOfControllersAlreadyProcessed.Add(jobTarget.Controller);

                            loggerConsole.Info("List of Controllers");

                            sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_CONTROLLERS_LIST];
                            if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                            {
                                fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                                numRowsToSkipInCSVFile = 0;
                            }
                            else
                            {
                                fromRow = sheet.Dimension.Rows + 1;
                                numRowsToSkipInCSVFile = 1;
                            }
                            readCSVFileIntoExcelRange(controllerReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                            loggerConsole.Info("List of Applications");

                            sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_APPLICATIONS_LIST];
                            if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                            {
                                fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                                numRowsToSkipInCSVFile = 0;
                            }
                            else
                            {
                                fromRow = sheet.Dimension.Rows + 1;
                                numRowsToSkipInCSVFile = 1;
                            }
                            readCSVFileIntoExcelRange(applicationsReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);
                        }

                        #endregion

                        #region Tiers

                        loggerConsole.Info("List of Tiers");

                        sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_TIERS_LIST];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(tiersReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Nodes

                        loggerConsole.Info("List of Nodes");

                        sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_NODES_LIST];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(nodesReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Backends

                        loggerConsole.Info("List of Backends");

                        sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LIST];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(backendsReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Business Transactions

                        loggerConsole.Info("List of Business Transactions");

                        sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LIST];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(businessTransactionsReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Service Endpoints

                        loggerConsole.Info("List of Service Endpoints");

                        sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LIST];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(serviceEndpointsReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Errors

                        loggerConsole.Info("List of Errors");

                        sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LIST];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(errorsReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                loggerConsole.Info("Finalize Detected Entities Report File");

                #region Controllers sheet

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_CONTROLLERS_LIST];
                loggerConsole.Info("Controllers Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_CONTROLLERS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["UserName"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                }

                #endregion

                #region Applications

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_APPLICATIONS_LIST];
                loggerConsole.Info("Applications Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_APPLICATIONS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInEntitiesReport(APPLICATION_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region Tiers

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_TIERS_LIST];
                loggerConsole.Info("Tiers Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_TIERS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInEntitiesReport(TIERS_TYPE_SHORT, sheet, table);

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_TIERS_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_TIERS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["AgentType"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["TierName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    //fieldD.Name = "Tiers Of Type";
                }

                #endregion

                #region Nodes

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_NODES_LIST];
                loggerConsole.Info("Nodes Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_NODES);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInEntitiesReport(NODES_TYPE_SHORT, sheet, table);

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_APPAGENT_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_NODES_TYPE_APPAGENT);
                    ExcelPivotTableField fieldF = pivot.PageFields.Add(pivot.Fields["AgentPresent"]);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["NodeName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["AgentType"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    fieldC = pivot.ColumnFields.Add(pivot.Fields["AgentVersion"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["TierName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    //fieldD.Name = "Agents Of Type";

                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_NODES_TYPE_MACHINEAGENT_PIVOT];
                    pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_NODES_TYPE_MACHINEAGENT);
                    fieldF = pivot.PageFields.Add(pivot.Fields["MachineAgentPresent"]);
                    fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["MachineName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldC = pivot.ColumnFields.Add(pivot.Fields["MachineAgentVersion"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    fieldD = pivot.DataFields.Add(pivot.Fields["TierName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    //fieldD.Name = "Machine Agents Of Type";
                }

                #endregion

                #region Backends

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LIST];
                loggerConsole.Info("Backends Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_BACKENDS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInEntitiesReport(BACKENDS_TYPE_SHORT, sheet, table);

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_TYPE_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_BACKENDS_TYPE);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BackendName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["BackendType"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["BackendName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    //fieldD.Name = "Backends Of Type";

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BACKENDS_LOCATION_PIVOT];
                    pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_BACKENDS_LOCATION);
                    fieldR = pivot.RowFields.Add(pivot.Fields["BackendType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BackendName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldD = pivot.DataFields.Add(pivot.Fields["BackendName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region Business Transactions

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LIST];
                loggerConsole.Info("Business Transactions Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_BUSINESS_TRANSACTIONS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInEntitiesReport(BUSINESS_TRANSACTIONS_TYPE_SHORT, sheet, table);

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_TYPE_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_BUSINESS_TRANSACTIONS_TYPE);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["BTType"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["BTName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    //fieldD.Name = "BTs Of Type";

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_LOCATION_PIVOT];
                    pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_BUSINESS_TRANSACTIONS_LOCATION_SHEET);
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldD = pivot.DataFields.Add(pivot.Fields["BTName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region Service Endpoints

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LIST];
                loggerConsole.Info("Service Endpoints Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_SERVICE_ENDPOINTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInEntitiesReport(SERVICE_ENDPOINTS_TYPE_SHORT, sheet, table);

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_TYPE_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_SERVICE_ENDPOINTS_TYPE);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["SEPName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["SEPType"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["SEPName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    //fieldD.Name = "SEPs Of Type";

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_SERVICE_ENDPOINTS_LOCATION_PIVOT];
                    pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_SERVICE_ENDPOINTS_LOCATION);
                    fieldR = pivot.RowFields.Add(pivot.Fields["SEPType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["SEPName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldD = pivot.DataFields.Add(pivot.Fields["SEPName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region Errors

                // Make table
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LIST];
                loggerConsole.Info("Errors Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_ERRORS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInEntitiesReport(ERRORS_TYPE_SHORT, sheet, table);

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_ERRORS_TYPE_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_ERRORS_TYPE);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ErrorName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["ErrorType"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["ErrorName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    //fieldD.Name = "Errors Of Type";

                    // Make pivot
                    sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_DETECTED_ENTITIES_SHEET_ERRORS_LOCATION_PIVOT_LOCATION];
                    pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_ENTITIES_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_ENTITIES_PIVOT_ERRORS_LOCATION);
                    fieldR = pivot.RowFields.Add(pivot.Fields["ErrorType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ErrorName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldD = pivot.DataFields.Add(pivot.Fields["ErrorName"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region TOC sheet

                // TOC sheet again
                sheet = excelDetectedEntities.Workbook.Worksheets[REPORT_SHEET_TOC];
                sheet.Cells[1, 1].Value = "Sheet Name";
                sheet.Cells[1, 2].Value = "# Entities";
                sheet.Cells[1, 3].Value = "Link";
                int rowNum = 1;
                foreach (ExcelWorksheet s in excelDetectedEntities.Workbook.Worksheets)
                {
                    rowNum++;
                    sheet.Cells[rowNum, 1].Value = s.Name;
                    sheet.Cells[rowNum, 3].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", s.Name);
                    if (s.Tables.Count > 0)
                    {
                        sheet.Cells[rowNum, 2].Value = s.Tables[0].Address.Rows - 1;
                    }
                }
                range = sheet.Cells[1, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_TOC);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(table.Columns["Sheet Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["# Entities"].Position + 1).AutoFit();

                #endregion

                #region Save file 

                // Report files
                string reportFileName = String.Format(
                    REPORT_DETECTED_ENTITIES_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To);
                string reportFilePath = Path.Combine(programOptions.OutputJobFolderPath, reportFileName);

                logger.Info("Saving Excel report {0}", reportFilePath);
                loggerConsole.Info("Saving Excel report {0}", reportFilePath);

                try
                {
                    // Save full report Excel files
                    excelDetectedEntities.SaveAs(new FileInfo(reportFilePath));
                }
                catch (InvalidOperationException ex)
                {
                    logger.Warn("Unable to save Excel file {0}", reportFilePath);
                    logger.Warn(ex);
                    loggerConsole.Warn("Unable to save Excel file {0}", reportFilePath);
                }

                #endregion

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepReportControllerAndApplicationConfiguration(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            loggerConsole.Fatal("TODO {0}({0:d})", jobStatus);
            return true;

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        loggerConsole.Fatal("TODO {0}({0:d})", jobStatus);

                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);

                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepReportApplicationAndEntityMetrics(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                loggerConsole.Info("Prepare Entity Metrics Report File");

                #region Prepare the report package

                // Prepare package
                ExcelPackage excelEntitiesMetrics = new ExcelPackage();
                excelEntitiesMetrics.Workbook.Properties.Author = String.Format("AppDynamics DEXTER {0}", Assembly.GetEntryAssembly().GetName().Version);
                excelEntitiesMetrics.Workbook.Properties.Title = "AppDynamics DEXTER Entity Metrics Report";
                excelEntitiesMetrics.Workbook.Properties.Subject = programOptions.JobName;

                excelEntitiesMetrics.Workbook.Properties.Comments = String.Format("Targets={0}\nFrom={1:o}\nTo={2:o}", jobConfiguration.Target.Count, jobConfiguration.Input.TimeRange.From, jobConfiguration.Input.TimeRange.To);

                #endregion

                #region Parameters sheet

                // Parameters sheet
                ExcelWorksheet sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_SHEET_PARAMETERS);

                var hyperLinkStyle = sheet.Workbook.Styles.CreateNamedStyle("HyperLinkStyle");
                hyperLinkStyle.Style.Font.UnderLineType = ExcelUnderLineType.Single;
                hyperLinkStyle.Style.Font.Color.SetColor(Color.Blue);

                int l = 1;
                sheet.Cells[l, 1].Value = "Table of Contents";
                sheet.Cells[l, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                l++; l++;
                sheet.Cells[l, 1].Value = "AppDynamics DEXTER Entity Metrics Report";
                l++; l++;
                sheet.Cells[l, 1].Value = "From";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "To";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Number of Hours Intervals";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.HourlyTimeRanges.Count;
                l++;
                sheet.Cells[l, 1].Value = "Export Metrics";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Metrics;
                l++;
                sheet.Cells[l, 1].Value = "Export Snapshots";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Snapshots;
                l++;
                sheet.Cells[l, 1].Value = "Export Flowmaps";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Flowmaps;
                l++;
                sheet.Cells[l, 1].Value = "Export Configuration";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Configuration;
                l++;
                sheet.Cells[l, 1].Value = "Export Events";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Events;
                l++;
                sheet.Cells[l, 1].Value = "Targets:";
                l++; l++;
                ExcelRangeBase range = sheet.Cells[l, 1].LoadFromCollection(from jobTarget in jobConfiguration.Target
                                                                            select new
                                                                            {
                                                                                Controller = jobTarget.Controller,
                                                                                UserName = jobTarget.UserName,
                                                                                Application = jobTarget.Application,
                                                                                ApplicationID = jobTarget.ApplicationID,
                                                                                Status = jobTarget.Status.ToString()
                                                                            }, true);
                ExcelTable table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_PARAMETERS_TARGETS);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(1).AutoFit();
                sheet.Column(2).AutoFit();
                sheet.Column(3).AutoFit();

                #endregion

                #region TOC sheet

                // Navigation sheet with link to other sheets
                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_SHEET_TOC);

                #endregion

                #region Entity sheets and their associated pivots

                // Entity sheets
                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_CONTROLLERS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_FULL);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_HOURLY);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_FULL);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_HOURLY);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_FULL);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_HOURLY);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_FULL);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_HOURLY);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_FULL);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_HOURLY);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_FULL);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_HOURLY);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_FULL);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelEntitiesMetrics.Workbook.Worksheets.Add(REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_HOURLY);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                #endregion

                List<string> listOfControllersAlreadyProcessed = new List<string>(jobConfiguration.Target.Count);

                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string metricsFolderPath = Path.Combine(applicationFolderPath, METRICS_FOLDER_NAME);

                        // Report files
                        string controllerReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_CONTROLLER_FILE_NAME);

                        // Metric paths and files
                        string metricsEntityFolderPath = String.Empty;
                        string entityFullRangeReportFilePath = String.Empty;
                        string entityHourlyRangeReportFilePath = String.Empty;
                        string entitiesFullRangeReportFilePath = String.Empty;
                        string entitiesHourlyRangeReportFilePath = String.Empty;

                        // Sheet row counters
                        int numRowsToSkipInCSVFile = 0;
                        int fromRow = 1;

                        #endregion

                        #region Controllers

                        // Only output this once per controller
                        if (listOfControllersAlreadyProcessed.Contains(jobTarget.Controller) == false)
                        {
                            listOfControllersAlreadyProcessed.Add(jobTarget.Controller);

                            loggerConsole.Info("List of Controllers");

                            sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_CONTROLLERS_LIST];
                            if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                            {
                                fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                                numRowsToSkipInCSVFile = 0;
                            }
                            else
                            {
                                fromRow = sheet.Dimension.Rows + 1;
                                numRowsToSkipInCSVFile = 1;
                            }
                            readCSVFileIntoExcelRange(controllerReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);
                        }

                        #endregion

                        #region Applications

                        loggerConsole.Info("List of Applications (Full)");

                        metricsEntityFolderPath = Path.Combine(metricsFolderPath, APPLICATION_TYPE_SHORT);

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_FULL];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                        readCSVFileIntoExcelRange(entityFullRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        loggerConsole.Info("List of Applications (Hourly)");

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_HOURLY];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                        readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Tiers

                        loggerConsole.Info("List of Tiers (Full)");

                        metricsEntityFolderPath = Path.Combine(metricsFolderPath, TIERS_TYPE_SHORT);

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_FULL];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                        readCSVFileIntoExcelRange(entityFullRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        loggerConsole.Info("List of Tiers (Hourly)");

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_HOURLY];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                        readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Nodes

                        loggerConsole.Info("List of Nodes (Full)");

                        metricsEntityFolderPath = Path.Combine(metricsFolderPath, NODES_TYPE_SHORT);

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_FULL];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                        readCSVFileIntoExcelRange(entityFullRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        loggerConsole.Info("List of Nodes (Hourly)");

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_HOURLY];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                        readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Backends

                        loggerConsole.Info("List of Backends (Full)");

                        metricsEntityFolderPath = Path.Combine(metricsFolderPath, BACKENDS_TYPE_SHORT);

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_FULL];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                        readCSVFileIntoExcelRange(entityFullRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        loggerConsole.Info("List of Backends (Hourly)");

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_HOURLY];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                        readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Business Transactions

                        loggerConsole.Info("List of Business Transactions (Full)");

                        metricsEntityFolderPath = Path.Combine(metricsFolderPath, BUSINESS_TRANSACTIONS_TYPE_SHORT);

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_FULL];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                        readCSVFileIntoExcelRange(entityFullRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        loggerConsole.Info("List of Business Transactions (Hourly)");

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_HOURLY];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                        readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Service Endpoints

                        loggerConsole.Info("List of Service Endpoints (Full)");

                        metricsEntityFolderPath = Path.Combine(metricsFolderPath, SERVICE_ENDPOINTS_TYPE_SHORT);

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_FULL];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                        readCSVFileIntoExcelRange(entityFullRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        loggerConsole.Info("List of Service Endpoints (Hourly)");

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_HOURLY];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                        readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Errors

                        loggerConsole.Info("List of Errors (Full)");

                        metricsEntityFolderPath = Path.Combine(metricsFolderPath, ERRORS_TYPE_SHORT);

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_FULL];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_FULLRANGE_FILE_NAME);
                        readCSVFileIntoExcelRange(entityFullRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        loggerConsole.Info("List of Errors (Hourly)");

                        sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_HOURLY];
                        if (sheet.Dimension.Rows < REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITIES_METRICS_HOURLY_FILE_NAME);
                        readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                loggerConsole.Info("Finalize Entity Metrics Report File");

                #region Controllers sheet

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_CONTROLLERS_LIST];
                loggerConsole.Info("Controllers Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_CONTROLLERS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["UserName"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                }

                #endregion

                #region Applications

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_FULL];
                loggerConsole.Info("Applications Sheet Full ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_APPLICATIONS_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(APPLICATION_TYPE_SHORT, sheet, table);
                }

                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_APPLICATIONS_HOURLY];
                loggerConsole.Info("Applications Sheet Hourly ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_APPLICATIONS_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(APPLICATION_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region Tiers

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_FULL];
                loggerConsole.Info("Tiers Sheet Full ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_TIERS_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(TIERS_TYPE_SHORT, sheet, table);
                }

                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_TIERS_HOURLY];
                loggerConsole.Info("Tiers Sheet Hourly ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_TIERS_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(TIERS_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region Nodes

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_FULL];
                loggerConsole.Info("Nodes Sheet Full ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_NODES_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(NODES_TYPE_SHORT, sheet, table);
                }

                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_NODES_HOURLY];
                loggerConsole.Info("Nodes Sheet Hourly ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_NODES_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(NODES_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region Backends

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_FULL];
                loggerConsole.Info("Backends Sheet Full ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_BACKENDS_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(BACKENDS_TYPE_SHORT, sheet, table);
                }

                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BACKENDS_HOURLY];
                loggerConsole.Info("Backends Sheet Hourly ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_BACKENDS_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(BACKENDS_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region Business Transactions

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_FULL];
                loggerConsole.Info("Business Transactions Sheet Full ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_BUSINESS_TRANSACTIONS_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(BUSINESS_TRANSACTIONS_TYPE_SHORT, sheet, table);
                }

                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_BUSINESS_TRANSACTIONS_HOURLY];
                loggerConsole.Info("Business Transactions Sheet Hourly ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_BUSINESS_TRANSACTIONS_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(BUSINESS_TRANSACTIONS_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region Service Endpoints

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_FULL];
                loggerConsole.Info("Service Endpoints Sheet Full ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_SERVICE_ENDPOINTS_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(SERVICE_ENDPOINTS_TYPE_SHORT, sheet, table);
                }

                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_SERVICE_ENDPOINTS_HOURLY];
                loggerConsole.Info("Service Endpoints Sheet Hourly ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_SERVICE_ENDPOINTS_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(SERVICE_ENDPOINTS_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region Errors

                // Make table
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_FULL];
                loggerConsole.Info("Errors Sheet Full ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_ERRORS_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(ERRORS_TYPE_SHORT, sheet, table);
                }

                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_METRICS_ALL_ENTITIES_SHEET_ERRORS_HOURLY];
                loggerConsole.Info("Errors Sheet Hourly ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_ERRORS_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(ERRORS_TYPE_SHORT, sheet, table);
                }

                #endregion

                #region TOC sheet

                // TOC sheet again
                sheet = excelEntitiesMetrics.Workbook.Worksheets[REPORT_SHEET_TOC];
                sheet.Cells[1, 1].Value = "Sheet Name";
                sheet.Cells[1, 2].Value = "# Rows";
                sheet.Cells[1, 3].Value = "Link";
                int rowNum = 1;
                foreach (ExcelWorksheet s in excelEntitiesMetrics.Workbook.Worksheets)
                {
                    rowNum++;
                    sheet.Cells[rowNum, 1].Value = s.Name;
                    sheet.Cells[rowNum, 3].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", s.Name);
                    if (s.Tables.Count > 0)
                    {
                        table = s.Tables[0];
                        sheet.Cells[rowNum, 2].Value = table.Address.Rows - 1;
                    }
                }
                range = sheet.Cells[1, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                table = sheet.Tables.Add(range, REPORT_METRICS_ALL_ENTITIES_TABLE_TOC);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(table.Columns["Sheet Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["# Rows"].Position + 1).AutoFit();

                #endregion

                #region Save file 

                // Report files
                string reportFileName = String.Format(
                    REPORT_METRICS_ALL_ENTITIES_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To);
                string reportFilePath = Path.Combine(programOptions.OutputJobFolderPath, reportFileName);

                logger.Info("Saving Excel report {0}", reportFilePath);
                loggerConsole.Info("Saving Excel report {0}", reportFilePath);

                try
                {
                    // Save full report Excel files
                    excelEntitiesMetrics.SaveAs(new FileInfo(reportFilePath));
                }
                catch (InvalidOperationException ex)
                {
                    logger.Warn("Unable to save Excel file {0}", reportFilePath);
                    logger.Warn(ex);
                    loggerConsole.Warn("Unable to save Excel file {0}", reportFilePath);
                }

                #endregion

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepReportEventsAndHealthRuleViolations(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                loggerConsole.Info("Prepare Events and Health Rule Violations Report File");

                #region Prepare the report package

                // Prepare package
                ExcelPackage excelDetectedEvents = new ExcelPackage();
                excelDetectedEvents.Workbook.Properties.Author = String.Format("AppDynamics DEXTER {0}", Assembly.GetEntryAssembly().GetName().Version);
                excelDetectedEvents.Workbook.Properties.Title = "AppDynamics DEXTER Events and Health Rule Violations Report";
                excelDetectedEvents.Workbook.Properties.Subject = programOptions.JobName;

                excelDetectedEvents.Workbook.Properties.Comments = String.Format("Targets={0}\nFrom={1:o}\nTo={2:o}", jobConfiguration.Target.Count, jobConfiguration.Input.TimeRange.From, jobConfiguration.Input.TimeRange.To);

                #endregion

                #region Parameters sheet

                // Parameters sheet
                ExcelWorksheet sheet = excelDetectedEvents.Workbook.Worksheets.Add(REPORT_SHEET_PARAMETERS);

                var hyperLinkStyle = sheet.Workbook.Styles.CreateNamedStyle("HyperLinkStyle");
                hyperLinkStyle.Style.Font.UnderLineType = ExcelUnderLineType.Single;
                hyperLinkStyle.Style.Font.Color.SetColor(Color.Blue);

                int l = 1;
                sheet.Cells[l, 1].Value = "Table of Contents";
                sheet.Cells[l, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                l++; l++;
                sheet.Cells[l, 1].Value = "AppDynamics DEXTER Events and Health Rule Violations Report";
                l++; l++;
                sheet.Cells[l, 1].Value = "From";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "To";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Number of Hours Intervals";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.HourlyTimeRanges.Count;
                l++;
                sheet.Cells[l, 1].Value = "Export Metrics";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Metrics;
                l++;
                sheet.Cells[l, 1].Value = "Export Snapshots";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Snapshots;
                l++;
                sheet.Cells[l, 1].Value = "Export Flowmaps";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Flowmaps;
                l++;
                sheet.Cells[l, 1].Value = "Export Configuration";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Configuration;
                l++;
                sheet.Cells[l, 1].Value = "Export Events";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Events;
                l++;
                sheet.Cells[l, 1].Value = "Targets:";
                l++; l++;
                ExcelRangeBase range = sheet.Cells[l, 1].LoadFromCollection(from jobTarget in jobConfiguration.Target
                                                                            select new
                                                                            {
                                                                                Controller = jobTarget.Controller,
                                                                                UserName = jobTarget.UserName,
                                                                                Application = jobTarget.Application,
                                                                                ApplicationID = jobTarget.ApplicationID,
                                                                                Status = jobTarget.Status.ToString()
                                                                            }, true);
                ExcelTable table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_PARAMETERS_TARGETS);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(1).AutoFit();
                sheet.Column(2).AutoFit();
                sheet.Column(3).AutoFit();

                #endregion

                #region TOC sheet

                // Navigation sheet with link to other sheets
                sheet = excelDetectedEvents.Workbook.Worksheets.Add(REPORT_SHEET_TOC);

                #endregion

                #region Entity sheets and their associated pivot

                // Entity sheets
                sheet = excelDetectedEvents.Workbook.Worksheets.Add(REPORT_DETECTED_EVENTS_SHEET_CONTROLLERS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_METRICS_ALL_ENTITIES_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEvents.Workbook.Worksheets.Add(REPORT_DETECTED_EVENTS_SHEET_EVENTS);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_EVENTS_SHEET_EVENTS_PIVOT);
                sheet.View.FreezePanes(REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEvents.Workbook.Worksheets.Add(REPORT_DETECTED_EVENTS_SHEET_EVENTS_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_EVENTS_SHEET_EVENTS);
                sheet.View.FreezePanes(REPORT_DETECTED_EVENTS_PIVOT_SHEET_START_PIVOT_AT + 2, 9);

                sheet = excelDetectedEvents.Workbook.Worksheets.Add(REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR_PIVOT);
                sheet.View.FreezePanes(REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelDetectedEvents.Workbook.Worksheets.Add(REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR);
                sheet.View.FreezePanes(REPORT_DETECTED_EVENTS_PIVOT_SHEET_START_PIVOT_AT + 2, 9);

                #endregion

                List<string> listOfControllersAlreadyProcessed = new List<string>(jobConfiguration.Target.Count);

                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string eventsFolderPath = Path.Combine(applicationFolderPath, EVENTS_FOLDER_NAME);

                        // Report files
                        string controllerReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_CONTROLLER_FILE_NAME);

                        // Sheet row counters
                        int numRowsToSkipInCSVFile = 0;
                        int fromRow = 1;

                        #endregion

                        #region Controllers

                        // Only output this once per controller
                        if (listOfControllersAlreadyProcessed.Contains(jobTarget.Controller) == false)
                        {
                            listOfControllersAlreadyProcessed.Add(jobTarget.Controller);

                            loggerConsole.Info("List of Controllers");

                            sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_CONTROLLERS_LIST];
                            if (sheet.Dimension.Rows < REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT)
                            {
                                fromRow = REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT;
                                numRowsToSkipInCSVFile = 0;
                            }
                            else
                            {
                                fromRow = sheet.Dimension.Rows + 1;
                                numRowsToSkipInCSVFile = 1;
                            }
                            readCSVFileIntoExcelRange(controllerReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);
                        }

                        #endregion

                        #region Events

                        loggerConsole.Info("List of Events");

                        string eventsFilePath = Path.Combine(eventsFolderPath, CONVERT_EVENTS_FILE_NAME);

                        sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_EVENTS];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(eventsFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Health Rule Violation Events

                        loggerConsole.Info("List of Health Rule Violation Events");

                        eventsFilePath = Path.Combine(eventsFolderPath, CONVERT_HEALTH_RULE_EVENTS_FILE_NAME);

                        sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR];
                        if (sheet.Dimension.Rows < REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(eventsFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                loggerConsole.Info("Finalize Events and Health Rule Violations Report File");

                #region Controllers sheet

                // Make table
                sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_CONTROLLERS_LIST];
                loggerConsole.Info("Controllers Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_EVENTS_TABLE_CONTROLLERS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["UserName"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                }

                #endregion

                #region Events

                // Make table
                sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_EVENTS];
                loggerConsole.Info("Events Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_EVENTS_TABLE_EVENTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["EventID"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["Occured"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["OccuredUtc"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["Summary"].Position + 1).Width = 35;
                    sheet.Column(table.Columns["Type"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["SubType"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TriggeredEntityType"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TriggeredEntityName"].Position + 1).Width = 20;
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["NodeLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["BTLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["EventLink"].Position + 1).AutoFit();

                    sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_EVENTS_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_EVENTS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_EVENTS_PIVOT_EVENTS_TYPE);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Type"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["SubType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Severity"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["NodeName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    // Adding this in Excel produces days, hours and minutes subfields, very nice
                    // Adding this in code produces dates formatted strings, not very good
                    //ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["Occured"]);
                    //fieldC.Compact = false;
                    //fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["EventID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region Health Rule Violation Events

                // Make table
                sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR];
                loggerConsole.Info("Health Rule Events Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_EVENTS_TABLE_HEALTH_RULE_VIOLATION_EVENTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["EventID"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["HealthRuleName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["EntityName"].Position + 1).Width = 20;
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["HealthRuleLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["EntityLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["EventLink"].Position + 1).AutoFit();

                    sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_EVENTS_HR_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_DETECTED_EVENTS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_DETECTED_EVENTS_PIVOT_HEALTH_RULE_VIOLATION_EVENTS_TYPE);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["HealthRuleName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Severity"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Status"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["EntityType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.ColumnFields.Add(pivot.Fields["EntityName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["EventID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region TOC sheet

                // TOC sheet again
                sheet = excelDetectedEvents.Workbook.Worksheets[REPORT_SHEET_TOC];
                sheet.Cells[1, 1].Value = "Sheet Name";
                sheet.Cells[1, 2].Value = "# Entities";
                sheet.Cells[1, 3].Value = "Link";
                int rowNum = 1;
                foreach (ExcelWorksheet s in excelDetectedEvents.Workbook.Worksheets)
                {
                    rowNum++;
                    sheet.Cells[rowNum, 1].Value = s.Name;
                    sheet.Cells[rowNum, 3].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", s.Name);
                    if (s.Tables.Count > 0)
                    {
                        table = s.Tables[0];
                        sheet.Cells[rowNum, 2].Value = table.Address.Rows - 1;
                    }
                }
                range = sheet.Cells[1, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_TOC);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(table.Columns["Sheet Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["# Entities"].Position + 1).AutoFit();

                #endregion

                #region Save file 

                // Report files
                string reportFileName = String.Format(
                    REPORT_DETECTED_EVENTS_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To);
                string reportFilePath = Path.Combine(programOptions.OutputJobFolderPath, reportFileName);

                logger.Info("Saving Excel report {0}", reportFilePath);
                loggerConsole.Info("Saving Excel report {0}", reportFilePath);

                try
                {
                    // Save full report Excel files
                    excelDetectedEvents.SaveAs(new FileInfo(reportFilePath));
                }
                catch (InvalidOperationException ex)
                {
                    logger.Warn("Unable to save Excel file {0}", reportFilePath);
                    logger.Warn(ex);
                    loggerConsole.Warn("Unable to save Excel file {0}", reportFilePath);
                }

                #endregion

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepReportIndividualApplicationAndEntityDetails(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                        //string metricsFolderPath = Path.Combine(applicationFolderPath, METRICS_FOLDER_NAME);

                        // Report files
                        string applicationReportFilePath = Path.Combine(applicationFolderPath, CONVERT_ENTITY_APPLICATION_FILE_NAME);
                        string tiersReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_TIERS_FILE_NAME);
                        string nodesReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_NODES_FILE_NAME);
                        string backendsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BACKENDS_FILE_NAME);
                        string businessTransactionsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_BUSINESS_TRANSACTIONS_FILE_NAME);
                        string serviceEndpointsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_SERVICE_ENDPOINTS_FILE_NAME);
                        string errorsReportFilePath = Path.Combine(entitiesFolderPath, CONVERT_ENTITY_ERRORS_FILE_NAME);

                        #endregion

                        #region Application

                        List<EntityApplication> applicationList = FileIOHelper.readListFromCSVFile<EntityApplication>(applicationReportFilePath, new ApplicationEntityReportMap());
                        if (applicationList != null && applicationList.Count > 0)
                        {
                            loggerConsole.Info("Metric Details for Application");

                            reportMetricDetailApplication(programOptions, jobConfiguration, jobTarget, applicationList[0]);
                        }

                        #endregion

                        #region Tier

                        List<EntityTier> tiersList = FileIOHelper.readListFromCSVFile<EntityTier>(tiersReportFilePath, new TierEntityReportMap());
                        if (tiersList != null)
                        {
                            loggerConsole.Info("Metric Details for Tiers ({0} entities)", tiersList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var tiersListChunks = tiersList.BreakListIntoChunks(METRIC_DETAILS_REPORT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<EntityTier>, int>(
                                    tiersListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_DETAILS_REPORT_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (tiersListChunk, loop, subtotal) =>
                                    {
                                        subtotal += reportMetricDetailTiers(programOptions, jobConfiguration, jobTarget, tiersListChunk, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = reportMetricDetailTiers(programOptions, jobConfiguration, jobTarget, tiersList, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Nodes

                        List<EntityNode> nodesList = FileIOHelper.readListFromCSVFile<EntityNode>(nodesReportFilePath, new NodeEntityReportMap());
                        if (nodesList != null)
                        {
                            loggerConsole.Info("Metric Details for Nodes ({0} entities)", nodesList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var nodesListChunks = nodesList.BreakListIntoChunks(METRIC_DETAILS_REPORT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<EntityNode>, int>(
                                    nodesListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_DETAILS_REPORT_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (nodesListChunk, loop, subtotal) =>
                                    {
                                        subtotal += reportMetricDetailNodes(programOptions, jobConfiguration, jobTarget, nodesListChunk, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = reportMetricDetailNodes(programOptions, jobConfiguration, jobTarget, nodesList, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Backends

                        List<EntityBackend> backendsList = FileIOHelper.readListFromCSVFile<EntityBackend>(backendsReportFilePath, new BackendEntityReportMap());
                        if (backendsList != null)
                        {
                            loggerConsole.Info("Metric Details for Backends ({0} entities)", backendsList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var backendsListChunks = backendsList.BreakListIntoChunks(METRIC_DETAILS_REPORT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<EntityBackend>, int>(
                                    backendsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_DETAILS_REPORT_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (backendsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += reportMetricDetailBackends(programOptions, jobConfiguration, jobTarget, backendsListChunk, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = reportMetricDetailBackends(programOptions, jobConfiguration, jobTarget, backendsList, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Business Transactions

                        List<EntityBusinessTransaction> businessTransactionsList = FileIOHelper.readListFromCSVFile<EntityBusinessTransaction>(businessTransactionsReportFilePath, new BusinessTransactionEntityReportMap());
                        if (businessTransactionsList != null)
                        {
                            loggerConsole.Info("Metric Details for Business Transactions ({0} entities)", businessTransactionsList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var businessTransactionsListChunks = businessTransactionsList.BreakListIntoChunks(METRIC_DETAILS_REPORT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<EntityBusinessTransaction>, int>(
                                    businessTransactionsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_DETAILS_REPORT_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (businessTransactionsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += reportMetricDetailBusinessTransactions(programOptions, jobConfiguration, jobTarget, businessTransactionsListChunk, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = reportMetricDetailBusinessTransactions(programOptions, jobConfiguration, jobTarget, businessTransactionsList, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Service Endpoints

                        List<EntityServiceEndpoint> serviceEndpointsList = FileIOHelper.readListFromCSVFile<EntityServiceEndpoint>(serviceEndpointsReportFilePath, new ServiceEndpointEntityReportMap());
                        if (serviceEndpointsList != null)
                        {
                            loggerConsole.Info("Metric Details for Service Endpoints ({0} entities)", serviceEndpointsList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var serviceEndpointsListChunks = serviceEndpointsList.BreakListIntoChunks(METRIC_DETAILS_REPORT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<EntityServiceEndpoint>, int>(
                                    serviceEndpointsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_DETAILS_REPORT_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (serviceEndpointsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += reportMetricDetailServiceEndpoints(programOptions, jobConfiguration, jobTarget, serviceEndpointsListChunk, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = reportMetricDetailServiceEndpoints(programOptions, jobConfiguration, jobTarget, serviceEndpointsList, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                        #region Errors

                        List<EntityError> errorsList = FileIOHelper.readListFromCSVFile<EntityError>(errorsReportFilePath, new ErrorEntityReportMap());
                        if (errorsList != null)
                        {
                            loggerConsole.Info("Metric Details for Errors ({0} entities)", errorsList.Count);

                            int j = 0;

                            if (programOptions.ProcessSequentially == false)
                            {
                                var errorsListChunks = errorsList.BreakListIntoChunks(METRIC_DETAILS_REPORT_NUMBER_OF_ENTITIES_TO_PROCESS_PER_THREAD);

                                Parallel.ForEach<List<EntityError>, int>(
                                    errorsListChunks,
                                    new ParallelOptions { MaxDegreeOfParallelism = METRIC_DETAILS_REPORT_EXTRACT_NUMBER_OF_THREADS },
                                    () => 0,
                                    (errorsListChunk, loop, subtotal) =>
                                    {
                                        subtotal += reportMetricDetailErrors(programOptions, jobConfiguration, jobTarget, errorsListChunk, false);
                                        return subtotal;
                                    },
                                    (finalResult) =>
                                    {
                                        j = Interlocked.Add(ref j, finalResult);
                                        Console.Write("[{0}].", j);
                                    }
                                );
                            }
                            else
                            {
                                j = reportMetricDetailErrors(programOptions, jobConfiguration, jobTarget, errorsList, true);
                            }

                            loggerConsole.Info("{0} entities", j);
                        }

                        #endregion

                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        private static bool stepReportSnapshots(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobStatus jobStatus)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                loggerConsole.Info("Prepare Snapshots Report File");

                #region Prepare the report package

                // Prepare package
                ExcelPackage excelSnapshots = new ExcelPackage();
                excelSnapshots.Workbook.Properties.Author = String.Format("AppDynamics DEXTER {0}", Assembly.GetEntryAssembly().GetName().Version);
                excelSnapshots.Workbook.Properties.Title = "AppDynamics DEXTER Snapshots Report";
                excelSnapshots.Workbook.Properties.Subject = programOptions.JobName;

                excelSnapshots.Workbook.Properties.Comments = String.Format("Targets={0}\nFrom={1:o}\nTo={2:o}", jobConfiguration.Target.Count, jobConfiguration.Input.TimeRange.From, jobConfiguration.Input.TimeRange.To);

                #endregion

                #region Parameters sheet

                // Parameters sheet
                ExcelWorksheet sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SHEET_PARAMETERS);

                var hyperLinkStyle = sheet.Workbook.Styles.CreateNamedStyle("HyperLinkStyle");
                hyperLinkStyle.Style.Font.UnderLineType = ExcelUnderLineType.Single;
                hyperLinkStyle.Style.Font.Color.SetColor(Color.Blue);

                int l = 1;
                sheet.Cells[l, 1].Value = "Table of Contents";
                sheet.Cells[l, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                l++; l++;
                sheet.Cells[l, 1].Value = "AppDynamics DEXTER Snapshots Report";
                l++; l++;
                sheet.Cells[l, 1].Value = "From";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "To";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded From (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (UTC)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Expanded To (Local)";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime().ToString("G");
                l++;
                sheet.Cells[l, 1].Value = "Number of Hours Intervals";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.HourlyTimeRanges.Count;
                l++;
                sheet.Cells[l, 1].Value = "Export Metrics";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Metrics;
                l++;
                sheet.Cells[l, 1].Value = "Export Snapshots";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Snapshots;
                l++;
                sheet.Cells[l, 1].Value = "Export Flowmaps";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Flowmaps;
                l++;
                sheet.Cells[l, 1].Value = "Export Configuration";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Configuration;
                l++;
                sheet.Cells[l, 1].Value = "Export Events";
                sheet.Cells[l, 2].Value = jobConfiguration.Input.Events;
                l++; 
                sheet.Cells[l, 1].Value = "Targets:";
                l++; l++;
                ExcelRangeBase range = sheet.Cells[l, 1].LoadFromCollection(from jobTarget in jobConfiguration.Target
                                                                            select new
                                                                            {
                                                                                Controller = jobTarget.Controller,
                                                                                UserName = jobTarget.UserName,
                                                                                Application = jobTarget.Application,
                                                                                ApplicationID = jobTarget.ApplicationID,
                                                                                Status = jobTarget.Status.ToString()
                                                                            }, true);
                ExcelTable table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_PARAMETERS_TARGETS);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(1).AutoFit();
                sheet.Column(2).AutoFit();
                sheet.Column(3).AutoFit();

                #endregion

                #region TOC sheet

                // Navigation sheet with link to other sheets
                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SHEET_TOC);

                #endregion

                #region Entity sheets and their associated pivot

                // Entity sheets
                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_CONTROLLERS_LIST);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_SNAPSHOTS);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_SNAPSHOTS_PIVOT);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_SNAPSHOTS_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_SNAPSHOTS);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_SEGMENTS);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_SEGMENTS_PIVOT);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_SEGMENTS_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_SEGMENTS);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_EXIT_CALLS);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_EXIT_CALLS_PIVOT);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_EXIT_CALLS_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_EXIT_CALLS);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT + 5, 7);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_SERVICE_ENDPOINT_CALLS);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS_PIVOT);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT + 1, 6);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Pivot";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA_PIVOT);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT + 1, 1);

                sheet = excelSnapshots.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA_PIVOT);
                sheet.Cells[1, 1].Value = "Table of Contents";
                sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheet.Cells[2, 1].Value = "See Table";
                sheet.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA);
                sheet.View.FreezePanes(REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT + 1, 7);

                #endregion

                List<string> listOfControllersAlreadyProcessed = new List<string>(jobConfiguration.Target.Count);

                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    try
                    {
                        #region Output status

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4}({5})", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, jobTarget.ApplicationID);

                        #endregion

                        #region Target state check

                        if (jobTarget.Status != JobTargetStatus.ConfigurationValid)
                        {
                            loggerConsole.Trace("Target in invalid state {0}, skipping", jobTarget.Status);

                            continue;
                        }

                        #endregion

                        #region Target step variables

                        // Various folders
                        string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
                        string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                        string snapshotsFolderPath = Path.Combine(applicationFolderPath, SNAPSHOTS_FOLDER_NAME);

                        // Report files
                        string controllerReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_CONTROLLER_FILE_NAME);
                        string snapshotsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_FILE_NAME);
                        string segmentsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_FILE_NAME);
                        string callExitsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_EXIT_CALLS_FILE_NAME);
                        string serviceEndpointCallsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_SERVICE_ENDPOINTS_CALLS_FILE_NAME);
                        string detectedErrorsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_DETECTED_ERRORS_FILE_NAME);
                        string businessDataFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_BUSINESS_DATA_FILE_NAME);

                        // Sheet row counters
                        int numRowsToSkipInCSVFile = 0;
                        int fromRow = 1;

                        #endregion

                        #region Controllers

                        // Only output this once per controller
                        if (listOfControllersAlreadyProcessed.Contains(jobTarget.Controller) == false)
                        {
                            listOfControllersAlreadyProcessed.Add(jobTarget.Controller);

                            loggerConsole.Info("List of Controllers");

                            sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_CONTROLLERS_LIST];
                            if (sheet.Dimension.Rows < REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                            {
                                fromRow = REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT;
                                numRowsToSkipInCSVFile = 0;
                            }
                            else
                            {
                                fromRow = sheet.Dimension.Rows + 1;
                                numRowsToSkipInCSVFile = 1;
                            }
                            readCSVFileIntoExcelRange(controllerReportFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);
                        }

                        #endregion

                        #region Snapshots

                        loggerConsole.Info("List of Snapshots");

                        sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SNAPSHOTS];
                        if (sheet.Dimension.Rows < REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(snapshotsFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Segments

                        loggerConsole.Info("List of Segments");

                        sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SEGMENTS];
                        if (sheet.Dimension.Rows < REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(segmentsFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Exit Calls

                        loggerConsole.Info("List of Exit Calls");

                        sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_EXIT_CALLS];
                        if (sheet.Dimension.Rows < REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(callExitsFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Service Endpoint Calls

                        loggerConsole.Info("List of Service Endpoint Calls");

                        sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SERVICE_ENDPOINT_CALLS];
                        if (sheet.Dimension.Rows < REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(serviceEndpointCallsFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Detected Errors

                        loggerConsole.Info("List of Detected Errors");

                        sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS];
                        if (sheet.Dimension.Rows < REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(detectedErrorsFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                        #region Business Data

                        loggerConsole.Info("List of Business Data");

                        sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA];
                        if (sheet.Dimension.Rows < REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                        {
                            fromRow = REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT;
                            numRowsToSkipInCSVFile = 0;
                        }
                        else
                        {
                            fromRow = sheet.Dimension.Rows + 1;
                            numRowsToSkipInCSVFile = 1;
                        }
                        readCSVFileIntoExcelRange(businessDataFilePath, numRowsToSkipInCSVFile, sheet, fromRow, 1);

                        #endregion

                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        logger.Info("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                        loggerConsole.Trace("{0}({0:d}): [{1}/{2}], {3} {4} duration {5:c} ({6} ms)", jobStatus, i + 1, jobConfiguration.Target.Count, jobTarget.Controller, jobTarget.Application, stopWatchTarget.Elapsed, stopWatchTarget.ElapsedMilliseconds);
                    }
                }

                #region Controllers sheet

                // Make table
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_DETECTED_EVENTS_SHEET_CONTROLLERS_LIST];
                loggerConsole.Info("Controllers Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_DETECTED_EVENTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_DETECTED_EVENTS_TABLE_CONTROLLERS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["UserName"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                }

                #endregion

                #region Snapshots

                // Make table
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SNAPSHOTS];
                loggerConsole.Info("Snapshots Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_SNAPSHOTS_TABLE_SNAPSHOTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["UserExperience"].Position + 1).Width = 10;
                    sheet.Column(table.Columns["RequestID"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["Occured"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["OccuredUtc"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["SnapshotLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["NodeLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["BTLink"].Position + 1).AutoFit();

                    sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SNAPSHOTS_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_SNAPSHOTS_PIVOT_SNAPSHOTS);
                    ExcelPivotTableField fieldF = pivot.PageFields.Add(pivot.Fields["HasErrors"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["CallGraphType"]);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["UserExperience"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region Segments

                // Make table
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SEGMENTS];
                loggerConsole.Info("Segments Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_SNAPSHOTS_TABLE_SEGMENTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["UserExperience"].Position + 1).Width = 10;
                    sheet.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["ParentSegmentID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["ParentTierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["Occured"].Position + 1).AutoFit();
                    sheet.Column(table.Columns["OccuredUtc"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["SnapshotLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["NodeLink"].Position + 1).AutoFit();
                    //sheet.Column(table.Columns["BTLink"].Position + 1).AutoFit();

                    sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SEGMENTS_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_SNAPSHOTS_PIVOT_SEGMENTS);
                    ExcelPivotTableField fieldF = pivot.PageFields.Add(pivot.Fields["HasErrors"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["CallGraphType"]);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["UserExperience"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["SegmentID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region Exit Calls

                // Make table
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_EXIT_CALLS];
                loggerConsole.Info("Exit Calls Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_SNAPSHOTS_TABLE_EXIT_CALLS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["ToEntityName"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["ExitType"].Position + 1).Width = 10;
                    sheet.Column(table.Columns["Detail"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["Method"].Position + 1).Width = 20;

                    sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_EXIT_CALLS_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT + 5, 1], range, REPORT_SNAPSHOTS_PIVOT_EXIT_CALLS);
                    ExcelPivotTableField fieldF = pivot.PageFields.Add(pivot.Fields["PropQueryType"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["PropStatementType"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["PropURL"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["PropServiceName"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["PropOperationName"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["PropName"]);
                    fieldF = pivot.PageFields.Add(pivot.Fields["PropAsync"]);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ExitType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Detail"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    fieldD = pivot.DataFields.Add(pivot.Fields["Duration"]);
                    fieldD.Function = DataFieldFunctions.Sum;
                }

                #endregion

                #region Service Endpoint Calls

                // Make table
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_SERVICE_ENDPOINT_CALLS];
                loggerConsole.Info("Exit Calls Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_SNAPSHOTS_TABLE_SERVICE_ENDPOINT_CALLS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["SepName"].Position + 1).Width = 20;
                }

                #endregion

                #region Detected Errors

                // Make table
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS];
                loggerConsole.Info("Detected Errors Sheet ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_SNAPSHOTS_TABLE_DETECTED_ERRORS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["ErrorName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ErrorMessage"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ErrorDetail"].Position + 1).Width = 20;

                    sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_DETECTED_ERRORS_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT - 3, 1], range, REPORT_SNAPSHOTS_PIVOT_DETECTED_ERRORS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ErrorMessage"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region Business Data

                // Make table
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA];
                loggerConsole.Info("Detected Business Data ({0} rows)", sheet.Dimension.Rows);
                if (sheet.Dimension.Rows > REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT)
                {
                    range = sheet.Cells[REPORT_SNAPSHOTS_LIST_SHEET_START_TABLE_AT, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                    table = sheet.Tables.Add(range, REPORT_SNAPSHOTS_TABLE_BUSINESS_DATA);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheet.Column(table.Columns["DataName"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["DataValue"].Position + 1).Width = 20;
                    sheet.Column(table.Columns["DataType"].Position + 1).Width = 10;

                    sheet = excelSnapshots.Workbook.Worksheets[REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA_PIVOT];
                    ExcelPivotTable pivot = sheet.PivotTables.Add(sheet.Cells[REPORT_SNAPSHOTS_PIVOT_SHEET_START_PIVOT_AT - 3, 1], range, REPORT_SNAPSHOTS_PIVOT_BUSINESS_DATA);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["DataType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["DataName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }

                #endregion

                #region TOC sheet

                // TOC sheet again
                sheet = excelSnapshots.Workbook.Worksheets[REPORT_SHEET_TOC];
                sheet.Cells[1, 1].Value = "Sheet Name";
                sheet.Cells[1, 2].Value = "# Entities";
                sheet.Cells[1, 3].Value = "Link";
                int rowNum = 1;
                foreach (ExcelWorksheet s in excelSnapshots.Workbook.Worksheets)
                {
                    rowNum++;
                    sheet.Cells[rowNum, 1].Value = s.Name;
                    sheet.Cells[rowNum, 3].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", s.Name);
                    if (s.Tables.Count > 0)
                    {
                        table = s.Tables[0];
                        sheet.Cells[rowNum, 2].Value = table.Address.Rows - 1;
                    }
                }
                range = sheet.Cells[1, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
                table = sheet.Tables.Add(range, REPORT_DETECTED_ENTITIES_TABLE_TOC);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(table.Columns["Sheet Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["# Entities"].Position + 1).AutoFit();

                #endregion

                #region Save file 

                // Report files
                string reportFileName = String.Format(
                    REPORT_SNAPSHOTS_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To);
                string reportFilePath = Path.Combine(programOptions.OutputJobFolderPath, reportFileName);

                logger.Info("Saving Excel report {0}", reportFilePath);
                loggerConsole.Info("Saving Excel report {0}", reportFilePath);

                try
                {
                    // Save full report Excel files
                    excelSnapshots.SaveAs(new FileInfo(reportFilePath));
                }
                catch (InvalidOperationException ex)
                {
                    logger.Warn("Unable to save Excel file {0}", reportFilePath);
                    logger.Warn(ex);
                    loggerConsole.Warn("Unable to save Excel file {0}", reportFilePath);
                }

                #endregion

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                logger.Info("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
                loggerConsole.Trace("{0}({0:d}): total duration {1:c} ({2} ms)", jobStatus, stopWatch.Elapsed, stopWatch.ElapsedMilliseconds);
            }
        }

        #endregion


        #region Metric extraction functions

        private static int extractMetricsApplication(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, string metricsFolderPath)
        {
            string metricsEntityFolderPath = Path.Combine(
                metricsFolderPath,
                APPLICATION_TYPE_SHORT);

            getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_APPLICATION, METRIC_ART_FULLNAME), METRIC_ART_FULLNAME, jobConfiguration, metricsEntityFolderPath);
            getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_APPLICATION, METRIC_CPM_FULLNAME), METRIC_CPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
            getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_APPLICATION, METRIC_EPM_FULLNAME), METRIC_EPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
            getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_APPLICATION, METRIC_EXCPM_FULLNAME), METRIC_EXCPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
            getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_APPLICATION, METRIC_HTTPEPM_FULLNAME), METRIC_HTTPEPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);

            return 1;
        }

        private static int extractMetricsTiers(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTTier> entityList, string metricsFolderPath, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTTier tier in entityList)
            {
                string metricsEntityFolderPath = Path.Combine(
                    metricsFolderPath,
                    TIERS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(tier.name, tier.id));

                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_TIER, tier.name, METRIC_ART_FULLNAME), METRIC_ART_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_TIER, tier.name, METRIC_CPM_FULLNAME), METRIC_CPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_TIER, tier.name, METRIC_EPM_FULLNAME), METRIC_EPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_TIER, tier.name, METRIC_EXCPM_FULLNAME), METRIC_EXCPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_TIER, tier.name, METRIC_HTTPEPM_FULLNAME), METRIC_HTTPEPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);

                FileIOHelper.writeObjectToFile(tier, Path.Combine(metricsEntityFolderPath, EXTRACT_ENTITY_NAME_FILE_NAME));

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractMetricsNodes(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTNode> entityList, string metricsFolderPath, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTNode node in entityList)
            {
                string metricsEntityFolderPath = Path.Combine(
                    metricsFolderPath,
                    NODES_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(node.tierName, node.tierId),
                    getShortenedEntityNameForFileSystem(node.name, node.id));

                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_NODE, node.tierName, node.name, METRIC_ART_FULLNAME), METRIC_ART_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_NODE, node.tierName, node.name, METRIC_CPM_FULLNAME), METRIC_CPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_NODE, node.tierName, node.name, METRIC_EPM_FULLNAME), METRIC_EPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_NODE, node.tierName, node.name, METRIC_EXCPM_FULLNAME), METRIC_EXCPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_NODE, node.tierName, node.name, METRIC_HTTPEPM_FULLNAME), METRIC_HTTPEPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);

                FileIOHelper.writeObjectToFile(node, Path.Combine(metricsEntityFolderPath, EXTRACT_ENTITY_NAME_FILE_NAME));

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractMetricsBackends(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTBackend> entityList, string metricsFolderPath, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTBackend backend in entityList)
            {
                string metricsEntityFolderPath = Path.Combine(
                    metricsFolderPath,
                    BACKENDS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(backend.name, backend.id));

                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_BACKEND, backend.name, METRIC_ART_FULLNAME), METRIC_ART_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_BACKEND, backend.name, METRIC_CPM_FULLNAME), METRIC_CPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_BACKEND, backend.name, METRIC_EPM_FULLNAME), METRIC_EPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);

                FileIOHelper.writeObjectToFile(backend, Path.Combine(metricsEntityFolderPath, EXTRACT_ENTITY_NAME_FILE_NAME));

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractMetricsBusinessTransactions(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTBusinessTransaction> entityList, string metricsFolderPath, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTBusinessTransaction businessTransaction in entityList)
            {
                string metricsEntityFolderPath = Path.Combine(
                    metricsFolderPath,
                    BUSINESS_TRANSACTIONS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(businessTransaction.tierName, businessTransaction.tierId),
                    getShortenedEntityNameForFileSystem(businessTransaction.name, businessTransaction.id));

                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_BUSINESS_TRANSACTION, businessTransaction.tierName, businessTransaction.name, METRIC_ART_FULLNAME), METRIC_ART_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_BUSINESS_TRANSACTION, businessTransaction.tierName, businessTransaction.name, METRIC_CPM_FULLNAME), METRIC_CPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_BUSINESS_TRANSACTION, businessTransaction.tierName, businessTransaction.name, METRIC_EPM_FULLNAME), METRIC_EPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);

                FileIOHelper.writeObjectToFile(businessTransaction, Path.Combine(metricsEntityFolderPath, EXTRACT_ENTITY_NAME_FILE_NAME));

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractMetricsServiceEndpoints(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTMetric> entityList, List<AppDRESTTier> tiersList, string metricsFolderPath, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTMetric serviceEndpoint in entityList)
            {
                // Parse SEP values
                string serviceEndpointTierName = serviceEndpoint.metricPath.Split('|')[1];
                long serviceEndpointTierID = -1;
                if (tiersList != null)
                {
                    // metricPath
                    // Service Endpoints|ECommerce-Services|/appdynamicspilot/rest|Calls per Minute
                    //                   ^^^^^^^^^^^^^^^^^^
                    //                   Tier
                    AppDRESTTier tierForThisEntity = tiersList.Where(tier => tier.name == serviceEndpointTierName).FirstOrDefault();
                    if (tierForThisEntity != null)
                    {
                        serviceEndpointTierID = tierForThisEntity.id;
                    }
                }
                // metricName
                // BTM|Application Diagnostic Data|SEP:4855|Calls per Minute
                //                                     ^^^^
                //                                     ID
                int serviceEndpointID = Convert.ToInt32(serviceEndpoint.metricName.Split('|')[2].Split(':')[1]);
                // metricPath
                // Service Endpoints|ECommerce-Services|/appdynamicspilot/rest|Calls per Minute
                //                                      ^^^^^^^^^^^^^^^^^^^^^^
                //                                      Name
                string serviceEndpointName = serviceEndpoint.metricPath.Split('|')[2];

                string metricsEntityFolderPath = Path.Combine(
                    metricsFolderPath,
                    SERVICE_ENDPOINTS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(serviceEndpointTierName, serviceEndpointTierID),
                    getShortenedEntityNameForFileSystem(serviceEndpointName, serviceEndpointID));

                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_SERVICE_ENDPOINT, serviceEndpointTierName, serviceEndpointName, METRIC_ART_FULLNAME), METRIC_ART_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_SERVICE_ENDPOINT, serviceEndpointTierName, serviceEndpointName, METRIC_CPM_FULLNAME), METRIC_CPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);
                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_SERVICE_ENDPOINT, serviceEndpointTierName, serviceEndpointName, METRIC_EPM_FULLNAME), METRIC_EPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);

                FileIOHelper.writeObjectToFile(serviceEndpoint, Path.Combine(metricsEntityFolderPath, EXTRACT_ENTITY_NAME_FILE_NAME));

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractMetricsErrors(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTMetric> entityList, List<AppDRESTTier> tiersList, string metricsFolderPath, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTMetric error in entityList)
            {
                // Parse Error values
                string errorTierName = error.metricPath.Split('|')[1];
                long errorTierID = -1;
                if (tiersList != null)
                {
                    // metricPath
                    // Errors|ECommerce-Services|CommunicationsException : EOFException|Errors per Minute
                    //        ^^^^^^^^^^^^^^^^^^
                    //        Tier
                    AppDRESTTier tierForThisEntity = tiersList.Where(tier => tier.name == errorTierName).FirstOrDefault();
                    if (tierForThisEntity != null)
                    {
                        errorTierID = tierForThisEntity.id;
                    }
                }
                // metricName
                // BTM|Application Diagnostic Data|Error:11626|Errors per Minute
                //                                       ^^^^^
                //                                       ID
                int errorID = Convert.ToInt32(error.metricName.Split('|')[2].Split(':')[1]);
                // metricPath
                // Errors|ECommerce-Services|CommunicationsException : EOFException|Errors per Minute
                //                           ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                //                           Name
                string errorName = error.metricPath.Split('|')[2];

                string metricsEntityFolderPath = Path.Combine(
                    metricsFolderPath,
                    ERRORS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(errorTierName, errorTierID),
                    getShortenedEntityNameForFileSystem(errorName, errorID));

                getMetricDataForMetricForAllRanges(controllerApi, jobTarget, String.Format(METRIC_PATH_ERROR, errorTierName, errorName, METRIC_EPM_FULLNAME), METRIC_EPM_FULLNAME, jobConfiguration, metricsEntityFolderPath);

                FileIOHelper.writeObjectToFile(error, Path.Combine(metricsEntityFolderPath, EXTRACT_ENTITY_NAME_FILE_NAME));

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static void getMetricDataForMetricForAllRanges(ControllerApi controllerApi, JobTarget jobTarget, string metricPath, string metricName, JobConfiguration jobConfiguration, string metricsEntityFolderPath)
        {
            string metricEntitySubFolderName = metricNameToShortMetricNameMapping[metricName];

            // Get the full range
            JobTimeRange jobTimeRange = jobConfiguration.Input.ExpandedTimeRange;

            logger.Info("Retrieving metric for Application {0}({1}), Metric='{2}', From {3:o}, To {4:o}", jobTarget.Application, jobTarget.ApplicationID, metricPath, jobTimeRange.From, jobTimeRange.To);

            string metricsJson = String.Empty;

            string metricsDataFilePath = Path.Combine(metricsEntityFolderPath, metricEntitySubFolderName, String.Format(EXTRACT_METRIC_FULL_FILE_NAME, jobTimeRange.From, jobTimeRange.To));
            if (File.Exists(metricsDataFilePath) == false)
            {
                // First range is the whole thing
                metricsJson = controllerApi.GetMetricData(
                    jobTarget.ApplicationID,
                    metricPath,
                    convertToUnixTimestamp(jobTimeRange.From),
                    convertToUnixTimestamp(jobTimeRange.To),
                    true);

                if (metricsJson != String.Empty) FileIOHelper.saveFileToFolder(metricsJson, metricsDataFilePath);
            }

            // Get the hourly time ranges
            for (int j = 0; j < jobConfiguration.Input.HourlyTimeRanges.Count; j++)
            {
                jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[j];

                logger.Info("Retrieving metric for Application {0}({1}), Metric='{2}', From {3:o}, To {4:o}", jobTarget.Application, jobTarget.ApplicationID, metricPath, jobTimeRange.From, jobTimeRange.To);

                metricsDataFilePath = Path.Combine(metricsEntityFolderPath, metricEntitySubFolderName, String.Format(EXTRACT_METRIC_HOUR_FILE_NAME, jobTimeRange.From, jobTimeRange.To));

                if (File.Exists(metricsDataFilePath) == false)
                {
                    // Subsequent ones are details
                    metricsJson = controllerApi.GetMetricData(
                        jobTarget.ApplicationID,
                        metricPath,
                        convertToUnixTimestamp(jobTimeRange.From),
                        convertToUnixTimestamp(jobTimeRange.To),
                        false);

                    if (metricsJson != String.Empty) FileIOHelper.saveFileToFolder(metricsJson, metricsDataFilePath);
                }
            }
        }

        #endregion

        #region Flowmap extraction functions

        private static int extractFlowmapsApplication(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, string metricsFolderPath, long fromTimeUnix, long toTimeUnix, long differenceInMinutes)
        {
            logger.Info("Retrieving flowmap for Application {0}, From {1:o}, To {2:o}", jobTarget.Application, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);

            string flowmapDataFilePath = Path.Combine(
                metricsFolderPath,
                APPLICATION_TYPE_SHORT,
                String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

            string flowmapJson = String.Empty;

            if (File.Exists(flowmapDataFilePath) == false)
            {
                flowmapJson = controllerApi.GetFlowmapApplication(jobTarget.ApplicationID, fromTimeUnix, toTimeUnix, differenceInMinutes);
                if (flowmapJson != String.Empty) FileIOHelper.saveFileToFolder(flowmapJson, flowmapDataFilePath);
            }

            return 1;
        }

        private static int extractFlowmapsTiers(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTTier> entityList, string metricsFolderPath, long fromTimeUnix, long toTimeUnix, long differenceInMinutes, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTTier tier in entityList)
            {
                logger.Info("Retrieving flowmap for Application {0}, Tier {1}, From {2:o}, To {3:o}", jobTarget.Application, tier.name, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);

                string flowmapDataFilePath = Path.Combine(
                    metricsFolderPath,
                    TIERS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(tier.name, tier.id),
                    String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                if (File.Exists(flowmapDataFilePath) == false)
                {
                    string flowmapJson = controllerApi.GetFlowmapTier(tier.id, fromTimeUnix, toTimeUnix, differenceInMinutes);
                    if (flowmapJson != String.Empty) FileIOHelper.saveFileToFolder(flowmapJson, flowmapDataFilePath);
                }

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractFlowmapsNodes(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTNode> entityList, string metricsFolderPath, long fromTimeUnix, long toTimeUnix, long differenceInMinutes, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTNode node in entityList)
            {
                logger.Info("Retrieving flowmap for Application {0}, Tier {1}, Node {2}, From {3:o}, To {4:o}", jobTarget.Application, node.tierName, node.name, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);

                string flowmapDataFilePath = Path.Combine(
                    metricsFolderPath,
                    NODES_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(node.tierName, node.tierId),
                    getShortenedEntityNameForFileSystem(node.name, node.id),
                    String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                if (File.Exists(flowmapDataFilePath) == false)
                {
                    string flowmapJson = controllerApi.GetFlowmapNode(node.id, fromTimeUnix, toTimeUnix, differenceInMinutes);
                    if (flowmapJson != String.Empty) FileIOHelper.saveFileToFolder(flowmapJson, flowmapDataFilePath);
                }

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractFlowmapsBackends(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTBackend> entityList, string metricsFolderPath, long fromTimeUnix, long toTimeUnix, long differenceInMinutes, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTBackend backend in entityList)
            {
                logger.Info("Retrieving flowmap for Application {0}, Backend {1}, From {2:o}, To {3:o}", jobTarget.Application, backend.name, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);

                string flowmapDataFilePath = Path.Combine(
                    metricsFolderPath,
                    BACKENDS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(backend.name, backend.id),
                    String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                if (File.Exists(flowmapDataFilePath) == false)
                {
                    string flowmapJson = controllerApi.GetFlowmapBackend(backend.id, fromTimeUnix, toTimeUnix, differenceInMinutes);
                    if (flowmapJson != String.Empty) FileIOHelper.saveFileToFolder(flowmapJson, flowmapDataFilePath);
                }

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int extractFlowmapsBusinessTransactions(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<AppDRESTBusinessTransaction> entityList, string metricsFolderPath, long fromTimeUnix, long toTimeUnix, long differenceInMinutes, bool progressToConsole)
        {
            int j = 0;

            foreach (AppDRESTBusinessTransaction businessTransaction in entityList)
            {
                logger.Info("Retrieving flowmap for Application {0}, Tier {1}, Business Transaction {2}, From {3:o}, To {4:o}", jobTarget.Application, businessTransaction.tierName, businessTransaction.name, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To);

                string flowmapDataFilePath = Path.Combine(
                    metricsFolderPath,
                    BUSINESS_TRANSACTIONS_TYPE_SHORT,
                    getShortenedEntityNameForFileSystem(businessTransaction.tierName, businessTransaction.tierId),
                    getShortenedEntityNameForFileSystem(businessTransaction.name, businessTransaction.id),
                    String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

                if (File.Exists(flowmapDataFilePath) == false)
                {
                    string flowmapJson = controllerApi.GetFlowmapBusinessTransaction(jobTarget.ApplicationID, businessTransaction.id, fromTimeUnix, toTimeUnix, differenceInMinutes);
                    if (flowmapJson != String.Empty) FileIOHelper.saveFileToFolder(flowmapJson, flowmapDataFilePath);
                }

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        #endregion

        #region Snapshot extraction functions

        private static int extractSnapshots(JobConfiguration jobConfiguration, JobTarget jobTarget, ControllerApi controllerApi, List<JToken> entityList, string snapshotsFolderPath, bool progressToConsole)
        {
           int j = 0;

            foreach (JToken snapshot in entityList)
            {
                // Only do first in chain
                if ((bool)snapshot["firstInChain"] == true)
                {
                    logger.Info("Retrieving snapshot for Application {0}, Tier {1}, Business Transaction {2}, RequestGUID {3}", jobTarget.Application, snapshot["applicationComponentName"], snapshot["businessTransactionName"], snapshot["requestGUID"]);

                    #region Target step variables

                    DateTime snapshotTime = convertFromUnixTimestamp((long)snapshot["serverStartTime"]);

                    string snapshotFolderPath = Path.Combine(
                        snapshotsFolderPath,
                        getShortenedEntityNameForFileSystem(snapshot["applicationComponentName"].ToString(), (long)snapshot["applicationComponentId"]),
                        getShortenedEntityNameForFileSystem(snapshot["businessTransactionName"].ToString(), (long)snapshot["businessTransactionId"]),
                        String.Format("{0:yyyyMMddHH}", snapshotTime),
                        userExperienceFolderNameMapping[snapshot["userExperience"].ToString()],
                        String.Format(SNAPSHOT_FOLDER_NAME, snapshot["requestGUID"], snapshotTime));

                    // Must strip out the milliseconds, because the segment list retrieval doesn't seem to like them in the datetimes
                    DateTime snapshotTimeFrom = snapshotTime.AddMinutes(-30).AddMilliseconds(snapshotTime.Millisecond * -1);
                    DateTime snapshotTimeTo = snapshotTime.AddMinutes(30).AddMilliseconds(snapshotTime.Millisecond * -1);

                    long fromTimeUnix = convertToUnixTimestamp(snapshotTimeFrom);
                    long toTimeUnix = convertToUnixTimestamp(snapshotTimeTo);
                    int differenceInMinutes = (int)(snapshotTimeTo - snapshotTimeFrom).TotalMinutes;

                    #endregion

                    #region Get Snapshot Flowmap

                    // Get snapshot flow map
                    string snapshotFlowmapDataFilePath = Path.Combine(snapshotFolderPath, EXTRACT_SNAPSHOT_FLOWMAP_FILE_NAME);

                    if (File.Exists(snapshotFlowmapDataFilePath) == false)
                    {
                        string snapshotFlowmapJson = controllerApi.GetFlowmapSnapshot(jobTarget.ApplicationID, (int)snapshot["businessTransactionId"], snapshot["requestGUID"].ToString(), fromTimeUnix, toTimeUnix, differenceInMinutes);
                        if (snapshotFlowmapJson != String.Empty) FileIOHelper.saveFileToFolder(snapshotFlowmapJson, snapshotFlowmapDataFilePath);
                    }

                    #endregion

                    #region Get List of Segments

                    // Get list of segments
                    string snapshotSegmentsDataFilePath = Path.Combine(snapshotFolderPath, EXTRACT_SNAPSHOT_SEGMENT_FILE_NAME);

                    if (File.Exists(snapshotSegmentsDataFilePath) == false)
                    {
                        string snapshotSegmentsJson = controllerApi.GetSnapshotSegments(snapshot["requestGUID"].ToString(), snapshotTimeFrom, snapshotTimeTo, differenceInMinutes);
                        if (snapshotSegmentsJson != String.Empty) FileIOHelper.saveFileToFolder(snapshotSegmentsJson, snapshotSegmentsDataFilePath);
                    }

                    #endregion

                    #region Get Details for Each Segment

                    JArray snapshotSegmentsList = FileIOHelper.loadJArrayFromFile(snapshotSegmentsDataFilePath);

                    if (snapshotSegmentsList != null)
                    {
                        // Get details for segment
                        foreach (JToken snapshotSegment in snapshotSegmentsList)
                        {
                            string snapshotSegmentDataFilePath = Path.Combine(snapshotFolderPath, String.Format(EXTRACT_SNAPSHOT_SEGMENT_DATA_FILE_NAME, snapshotSegment["id"]));

                            if (File.Exists(snapshotSegmentDataFilePath) == false)
                            {
                                string snapshotSegmentJson = controllerApi.GetSnapshotSegmentDetails((long)snapshotSegment["id"], fromTimeUnix, toTimeUnix, differenceInMinutes);
                                if (snapshotSegmentJson != String.Empty) FileIOHelper.saveFileToFolder(snapshotSegmentJson, snapshotSegmentDataFilePath);
                            }
                        }

                        // Get errors for segment
                        foreach (JToken snapshotSegment in snapshotSegmentsList)
                        {
                            string snapshotSegmentErrorFilePath = Path.Combine(snapshotFolderPath, String.Format(EXTRACT_SNAPSHOT_SEGMENT_ERROR_FILE_NAME, snapshotSegment["id"]));

                            if (File.Exists(snapshotSegmentErrorFilePath) == false)
                            {
                                string snapshotSegmentJson = controllerApi.GetSnapshotSegmentErrors((long)snapshotSegment["id"], fromTimeUnix, toTimeUnix, differenceInMinutes);
                                if (snapshotSegmentJson != String.Empty)
                                {
                                    // "[ ]" == empty data. Don't create the file
                                    if (snapshotSegmentJson.Length > 3)
                                    {
                                        FileIOHelper.saveFileToFolder(snapshotSegmentJson, snapshotSegmentErrorFilePath);
                                    }
                                }
                            }
                        }

                        // Get call graphs for segment
                        foreach (JToken snapshotSegment in snapshotSegmentsList)
                        {
                            string snapshotSegmentCallGraphFilePath = Path.Combine(snapshotFolderPath, String.Format(EXTRACT_SNAPSHOT_SEGMENT_CALLGRAPH_FILE_NAME, snapshotSegment["id"]));

                            if (File.Exists(snapshotSegmentCallGraphFilePath) == false)
                            {
                                string snapshotSegmentJson = controllerApi.GetSnapshotSegmentCallGraph((long)snapshotSegment["id"], fromTimeUnix, toTimeUnix, differenceInMinutes);
                                if (snapshotSegmentJson != String.Empty) FileIOHelper.saveFileToFolder(snapshotSegmentJson, snapshotSegmentCallGraphFilePath);
                            }
                        }
                    }

                    #endregion
                }

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        #endregion


        #region Metric detail conversion functions

        private static bool fillFullRangeMetricEntityRow(EntityBase entityRow, string metricsEntityFolderPath, JobTimeRange jobTimeRange)
        {
            string fullRangeFileName = String.Format(EXTRACT_METRIC_FULL_FILE_NAME, jobTimeRange.From, jobTimeRange.To);

            logger.Info("Retrieving full range metrics for Entity Type {0} from path={1}, file {2}, From={3:o}, To={4:o}", entityRow.GetType().Name, metricsEntityFolderPath, fullRangeFileName, jobTimeRange.From, jobTimeRange.To);

            entityRow.Duration = (int)(jobTimeRange.To - jobTimeRange.From).Duration().TotalMinutes;
            entityRow.From = jobTimeRange.From.ToLocalTime();
            entityRow.To = jobTimeRange.To.ToLocalTime();
            entityRow.FromUtc = jobTimeRange.From;
            entityRow.ToUtc = jobTimeRange.To;

            #region Read and convert metrics

            if (entityRow.MetricsIDs == null) { entityRow.MetricsIDs = new List<long>(3); }

            string metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_ART_SHORTNAME);
            string metricsDataFilePath = Path.Combine(metricsDataFolderPath, fullRangeFileName);
            string entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.ART = metricData[0].metricValues[0].value;
                        entityRow.TimeTotal = metricData[0].metricValues[0].sum;
                    }

                    if (File.Exists(entityMetricSummaryReportFilePath) == false)
                    {
                        List<MetricSummary> metricSummaries = convertMetricSummaryToTypedListForCSV(metricData[0], entityRow, jobTimeRange);
                        FileIOHelper.writeListToCSVFile(metricSummaries, new MetricSummaryMetricReportMap(), entityMetricSummaryReportFilePath, false);
                    }

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_CPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, fullRangeFileName);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.CPM = metricData[0].metricValues[0].value;
                        entityRow.Calls = metricData[0].metricValues[0].sum;
                    }

                    if (File.Exists(entityMetricSummaryReportFilePath) == false)
                    {
                        List<MetricSummary> metricSummaries = convertMetricSummaryToTypedListForCSV(metricData[0], entityRow, jobTimeRange);
                        FileIOHelper.writeListToCSVFile(metricSummaries, new MetricSummaryMetricReportMap(), entityMetricSummaryReportFilePath, false);
                    }

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, fullRangeFileName);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.EPM = metricData[0].metricValues[0].value;
                        entityRow.Errors = metricData[0].metricValues[0].sum;
                        entityRow.ErrorsPercentage = Math.Round((double)(double)entityRow.Errors / (double)entityRow.Calls * 100, 2);
                        if (Double.IsNaN(entityRow.ErrorsPercentage) == true) entityRow.ErrorsPercentage = 0;
                    }

                    if (File.Exists(entityMetricSummaryReportFilePath) == false)
                    {
                        List<MetricSummary> metricSummaries = convertMetricSummaryToTypedListForCSV(metricData[0], entityRow, jobTimeRange);
                        FileIOHelper.writeListToCSVFile(metricSummaries, new MetricSummaryMetricReportMap(), entityMetricSummaryReportFilePath, false);
                    }

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EXCPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, fullRangeFileName);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.EXCPM = metricData[0].metricValues[0].value;
                        entityRow.Exceptions = metricData[0].metricValues[0].sum;
                    }

                    if (File.Exists(entityMetricSummaryReportFilePath) == false)
                    {
                        List<MetricSummary> metricSummaries = convertMetricSummaryToTypedListForCSV(metricData[0], entityRow, jobTimeRange);
                        FileIOHelper.writeListToCSVFile(metricSummaries, new MetricSummaryMetricReportMap(), entityMetricSummaryReportFilePath, false);
                    }

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_HTTPEPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, fullRangeFileName);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.HTTPEPM = metricData[0].metricValues[0].value;
                        entityRow.HttpErrors = metricData[0].metricValues[0].sum;
                    }

                    if (File.Exists(entityMetricSummaryReportFilePath) == false)
                    {
                        List<MetricSummary> metricSummaries = convertMetricSummaryToTypedListForCSV(metricData[0], entityRow, jobTimeRange);
                        FileIOHelper.writeListToCSVFile(metricSummaries, new MetricSummaryMetricReportMap(), entityMetricSummaryReportFilePath, false);
                    }

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            if (entityRow.ART == 0 &&
                entityRow.CPM == 0 &&
                entityRow.EPM == 0 &&
                entityRow.EXCPM == 0 &&
                entityRow.HTTPEPM == 0)
            {
                entityRow.HasActivity = false;
            }
            else
            {
                entityRow.HasActivity = true;
            }

            #endregion

            updateEntityWithDeeplinks(entityRow, jobTimeRange);

            return true;
        }

        private static bool fillHourlyRangeMetricEntityRowAndConvertMetricsToCSV(EntityBase entityRow, string metricsEntityFolderPath, JobTimeRange jobTimeRange, int timeRangeIndex)
        {
            string hourRangeFileName = String.Format(EXTRACT_METRIC_HOUR_FILE_NAME, jobTimeRange.From, jobTimeRange.To);

            logger.Info("Retrieving hourly range metrics for Entity Type {0} from path={1}, file {2}, From={3:o}, To={4:o}", entityRow.GetType().Name, metricsEntityFolderPath, hourRangeFileName, jobTimeRange.From, jobTimeRange.To);

            entityRow.Duration = (int)(jobTimeRange.To - jobTimeRange.From).Duration().TotalMinutes;
            entityRow.From = jobTimeRange.From.ToLocalTime();
            entityRow.To = jobTimeRange.To.ToLocalTime();
            entityRow.FromUtc = jobTimeRange.From;
            entityRow.ToUtc = jobTimeRange.To;

            #region Read and convert metrics

            if (entityRow.MetricsIDs == null) { entityRow.MetricsIDs = new List<long>(5); }

            string metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_ART_SHORTNAME);
            string metricsDataFilePath = Path.Combine(metricsDataFolderPath, hourRangeFileName);
            string entityMetricReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            bool appendRecordsToExistingFile = true;
            if (timeRangeIndex == 0) { appendRecordsToExistingFile = false; }

            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.ART = (long)Math.Round((double)((double)metricData[0].metricValues.Sum(mv => mv.sum) / (double)metricData[0].metricValues.Sum(mv => mv.count)), 0);
                        entityRow.TimeTotal = metricData[0].metricValues.Sum(mv => mv.sum);
                    }

                    List<MetricValue> metricValues = convertMetricValueToTypedListForCSV(metricData[0]);
                    FileIOHelper.writeListToCSVFile(metricValues, new MetricValueMetricReportMap(), entityMetricReportFilePath, appendRecordsToExistingFile);

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_CPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, hourRangeFileName);
            entityMetricReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.CPM = (long)Math.Round((double)((double)metricData[0].metricValues.Sum(mv => mv.sum) / (double)entityRow.Duration), 0);
                        entityRow.Calls = metricData[0].metricValues.Sum(mv => mv.sum);
                    }

                    List<MetricValue> metricValues = convertMetricValueToTypedListForCSV(metricData[0]);
                    FileIOHelper.writeListToCSVFile(metricValues, new MetricValueMetricReportMap(), entityMetricReportFilePath, appendRecordsToExistingFile);

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, hourRangeFileName);
            entityMetricReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.EPM = (long)Math.Round((double)((double)metricData[0].metricValues.Sum(mv => mv.sum) / (double)entityRow.Duration), 0);
                        entityRow.Errors = metricData[0].metricValues.Sum(mv => mv.sum);
                        entityRow.ErrorsPercentage = Math.Round((double)(double)entityRow.Errors / (double)entityRow.Calls * 100, 2);
                        if (Double.IsNaN(entityRow.ErrorsPercentage) == true) entityRow.ErrorsPercentage = 0;
                    }

                    List<MetricValue> metricValues = convertMetricValueToTypedListForCSV(metricData[0]);
                    FileIOHelper.writeListToCSVFile(metricValues, new MetricValueMetricReportMap(), entityMetricReportFilePath, appendRecordsToExistingFile);

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EXCPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, hourRangeFileName);
            entityMetricReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.EXCPM = (long)Math.Round((double)((double)metricData[0].metricValues.Sum(mv => mv.sum) / (double)entityRow.Duration), 0);
                        entityRow.Exceptions = metricData[0].metricValues.Sum(mv => mv.sum);
                    }

                    List<MetricValue> metricValues = convertMetricValueToTypedListForCSV(metricData[0]);
                    FileIOHelper.writeListToCSVFile(metricValues, new MetricValueMetricReportMap(), entityMetricReportFilePath, appendRecordsToExistingFile);

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_HTTPEPM_SHORTNAME);
            metricsDataFilePath = Path.Combine(metricsDataFolderPath, hourRangeFileName);
            entityMetricReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(metricsDataFilePath) == true)
            {
                List<AppDRESTMetric> metricData = FileIOHelper.loadListOfObjectsFromFile<AppDRESTMetric>(metricsDataFilePath);
                if (metricData != null && metricData.Count > 0)
                {
                    if (metricData[0].metricValues.Count > 0)
                    {
                        entityRow.HTTPEPM = (long)Math.Round((double)((double)metricData[0].metricValues.Sum(mv => mv.sum) / (double)entityRow.Duration), 0);
                        entityRow.HttpErrors = metricData[0].metricValues.Sum(mv => mv.sum);
                    }

                    List<MetricValue> metricValues = convertMetricValueToTypedListForCSV(metricData[0]);
                    FileIOHelper.writeListToCSVFile(metricValues, new MetricValueMetricReportMap(), entityMetricReportFilePath, appendRecordsToExistingFile);

                    entityRow.MetricsIDs.Add(metricData[0].metricId);
                }
            }

            if (entityRow.ART == 0 &&
                entityRow.CPM == 0 &&
                entityRow.EPM == 0 &&
                entityRow.EXCPM == 0 &&
                entityRow.HTTPEPM == 0)
            {
                entityRow.HasActivity = false;
            }
            else
            {
                entityRow.HasActivity = true;
            }

            #endregion

            // Add link to the metrics
            updateEntityWithDeeplinks(entityRow, jobTimeRange);

            return true;
        }

        private static bool updateEntityWithDeeplinks(EntityBase entityRow)
        {
            return updateEntityWithDeeplinks(entityRow, null);
        }

        private static bool updateEntityWithDeeplinks(EntityBase entityRow, JobTimeRange jobTimeRange)
        {
            // Decide what kind of timerange
            string DEEPLINK_THIS_TIMERANGE = DEEPLINK_TIMERANGE_LAST_15_MINUTES;
            if (jobTimeRange != null)
            {
                long fromTimeUnix = convertToUnixTimestamp(jobTimeRange.From);
                long toTimeUnix = convertToUnixTimestamp(jobTimeRange.To);
                long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
                DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);
            }

            // Determine what kind of entity we are dealing with and adjust accordingly
            string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
            long entityIdForMetricBrowser = entityRow.ApplicationID;
            if (entityRow is EntityApplication)
            {
                entityRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, entityRow.Controller, DEEPLINK_THIS_TIMERANGE);
                entityRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, entityRow.Controller, entityRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
            }
            else if (entityRow is EntityTier)
            {
                entityRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, entityRow.Controller, DEEPLINK_THIS_TIMERANGE);
                entityRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, entityRow.Controller, entityRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                entityRow.TierLink = String.Format(DEEPLINK_TIER, entityRow.Controller, entityRow.ApplicationID, entityRow.TierID, DEEPLINK_THIS_TIMERANGE);
                deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                entityIdForMetricBrowser = entityRow.TierID;
            }
            else if (entityRow is EntityNode)
            {
                entityRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, entityRow.Controller, DEEPLINK_THIS_TIMERANGE);
                entityRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, entityRow.Controller, entityRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                entityRow.TierLink = String.Format(DEEPLINK_TIER, entityRow.Controller, entityRow.ApplicationID, entityRow.TierID, DEEPLINK_THIS_TIMERANGE);
                entityRow.NodeLink = String.Format(DEEPLINK_NODE, entityRow.Controller, entityRow.ApplicationID, entityRow.NodeID, DEEPLINK_THIS_TIMERANGE);
                deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_NODE_TARGET_METRIC_ID;
                entityIdForMetricBrowser = entityRow.NodeID;
            }
            else if (entityRow is EntityBackend)
            {
                entityRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, entityRow.Controller, DEEPLINK_THIS_TIMERANGE);
                entityRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, entityRow.Controller, entityRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                ((EntityBackend)entityRow).BackendLink = String.Format(DEEPLINK_BACKEND, entityRow.Controller, entityRow.ApplicationID, ((EntityBackend)entityRow).BackendID, DEEPLINK_THIS_TIMERANGE);
            }
            else if (entityRow is EntityBusinessTransaction)
            {
                entityRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, entityRow.Controller, DEEPLINK_THIS_TIMERANGE);
                entityRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, entityRow.Controller, entityRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                entityRow.TierLink = String.Format(DEEPLINK_TIER, entityRow.Controller, entityRow.ApplicationID, entityRow.TierID, DEEPLINK_THIS_TIMERANGE);
                ((EntityBusinessTransaction)entityRow).BTLink = String.Format(DEEPLINK_BUSINESS_TRANSACTION, entityRow.Controller, entityRow.ApplicationID, ((EntityBusinessTransaction)entityRow).BTID, DEEPLINK_THIS_TIMERANGE);
                deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                entityIdForMetricBrowser = entityRow.TierID;
            }
            else if (entityRow is EntityServiceEndpoint)
            {
                entityRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, entityRow.Controller, DEEPLINK_THIS_TIMERANGE);
                entityRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, entityRow.Controller, entityRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                entityRow.TierLink = String.Format(DEEPLINK_TIER, entityRow.Controller, entityRow.ApplicationID, entityRow.TierID, DEEPLINK_THIS_TIMERANGE);
                ((EntityServiceEndpoint)entityRow).SEPLink = String.Format(DEEPLINK_SERVICE_ENDPOINT, entityRow.Controller, entityRow.ApplicationID, entityRow.TierID, ((EntityServiceEndpoint)entityRow).SEPID, DEEPLINK_THIS_TIMERANGE);
            }
            else if (entityRow is EntityError)
            {
                entityRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, entityRow.Controller, DEEPLINK_THIS_TIMERANGE);
                entityRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, entityRow.Controller, entityRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                entityRow.TierLink = String.Format(DEEPLINK_TIER, entityRow.Controller, entityRow.ApplicationID, entityRow.TierID, DEEPLINK_THIS_TIMERANGE);
                ((EntityError)entityRow).ErrorLink = String.Format(DEEPLINK_ERROR, entityRow.Controller, entityRow.ApplicationID, ((EntityError)entityRow).ErrorID, DEEPLINK_THIS_TIMERANGE);
            }

            if (entityRow.MetricsIDs != null && entityRow.MetricsIDs.Count > 0)
            {
                StringBuilder sb = new StringBuilder(128);
                foreach (int metricID in entityRow.MetricsIDs)
                {
                    sb.Append(String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricID));
                    sb.Append(",");
                }
                sb.Remove(sb.Length - 1, 1);
                entityRow.MetricLink = String.Format(DEEPLINK_METRIC, entityRow.Controller, entityRow.ApplicationID, sb.ToString(), DEEPLINK_THIS_TIMERANGE);
            }

            return true;
        }

        private static void updateEntitiesWithReportDetailLinksApplication(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityApplication> entityList)
        {
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));

            for (int i = 0; i < entityList.Count; i++)
            {
                EntityBase entityRow = entityList[i];

                string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID));
                string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

                entityRow.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, entityRow).Substring(programOptions.OutputJobFolderPath.Length + 1));
            }
        }

        private static void updateEntitiesWithReportDetailLinksTiers(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityTier> entityList)
        {
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));

            for (int i = 0; i < entityList.Count; i++)
            {
                EntityBase entityRow = entityList[i];

                string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

                entityRow.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, entityRow).Substring(programOptions.OutputJobFolderPath.Length + 1));
            }
        }

        private static void updateEntitiesWithReportDetailLinksNodes(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityNode> entityList)
        {
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));

            for (int i = 0; i < entityList.Count; i++)
            {
                EntityBase entityRow = entityList[i];

                string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

                entityRow.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, entityRow).Substring(programOptions.OutputJobFolderPath.Length + 1));
            }
        }

        private static void updateEntitiesWithReportDetailLinksBackends(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityBackend> entityList)
        {
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));

            for (int i = 0; i < entityList.Count; i++)
            {
                EntityBase entityRow = entityList[i];

                string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

                entityRow.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, entityRow).Substring(programOptions.OutputJobFolderPath.Length + 1));
            }
        }

        private static void updateEntitiesWithReportDetailLinksBusinessTransactions(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityBusinessTransaction> entityList)
        {
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));

            for (int i = 0; i < entityList.Count; i++)
            {
                EntityBase entityRow = entityList[i];

                string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

                entityRow.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, entityRow).Substring(programOptions.OutputJobFolderPath.Length + 1));
            }
        }

        private static void updateEntitiesWithReportDetailLinksServiceEndpoints(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityServiceEndpoint> entityList)
        {
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));

            for (int i = 0; i < entityList.Count; i++)
            {
                EntityBase entityRow = entityList[i];

                string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
                string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
                string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

                entityRow.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, entityRow).Substring(programOptions.OutputJobFolderPath.Length + 1));
            }
        }

        private static void updateEntitiesWithReportDetailLinksErrors(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityError> entityList)
        {
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            for (int i = 0; i < entityList.Count; i++)
            {
                EntityBase entityRow = entityList[i];
                entityRow.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, entityRow).Substring(programOptions.OutputJobFolderPath.Length + 1));
            }
        }

        private static List<MetricValue> convertMetricValueToTypedListForCSV(AppDRESTMetric metricValueObject)
        {
            List<MetricValue> metricValues = new List<MetricValue>(metricValueObject.metricValues.Count);
            foreach (AppDRESTMetricValue mv in metricValueObject.metricValues)
            {
                MetricValue metricValue = new MetricValue();
                metricValue.EventTimeStampUtc = convertFromUnixTimestamp(mv.startTimeInMillis);
                metricValue.EventTimeStamp = metricValue.EventTimeStampUtc.ToLocalTime();
                metricValue.EventTime = metricValue.EventTimeStamp;
                metricValue.Count = mv.count;
                metricValue.Min = mv.min;
                metricValue.Max = mv.max;
                metricValue.Occurences = mv.occurrences;
                metricValue.Sum = mv.sum;
                metricValue.Value = mv.value;

                metricValue.MetricID = metricValueObject.metricId;
                switch (metricValueObject.frequency)
                {
                    case "SIXTY_MIN":
                        {
                            metricValue.MetricResolution = MetricResolution.SIXTY_MIN;
                            break;
                        }
                    case "TEN_MIN":
                        {
                            metricValue.MetricResolution = MetricResolution.TEN_MIN;
                            break;
                        }
                    case "ONE_MIN":
                        {
                            metricValue.MetricResolution = MetricResolution.ONE_MIN;
                            break;
                        }
                    default:
                        {
                            metricValue.MetricResolution = MetricResolution.ONE_MIN;
                            break;
                        }
                }
                metricValues.Add(metricValue);
            }

            return metricValues;
        }

        private static List<MetricSummary> convertMetricSummaryToTypedListForCSV(AppDRESTMetric metricValueObject, EntityBase entityRow, JobTimeRange jobTimeRange)
        {
            List<MetricSummary> metricSummaries = new List<MetricSummary>();
            metricSummaries.Add(new MetricSummary() {
                PropertyName = "Controller",
                PropertyValue = entityRow.Controller,
                Link = entityRow.ControllerLink });
            metricSummaries.Add(new MetricSummary() {
                PropertyName = "Application",
                PropertyValue = entityRow.ApplicationName,
                Link = entityRow.ApplicationLink });

            string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
            long entityIdForMetricBrowser = entityRow.ApplicationID;
            if (entityRow is EntityApplication)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "EntityType", PropertyValue = "Application" });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ApplicationID", PropertyValue = entityRow.ApplicationID });
            }
            else if (entityRow is EntityTier)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "EntityType", PropertyValue = "Tier" });
                metricSummaries.Add(new MetricSummary() {
                    PropertyName = "Tier",
                    PropertyValue = entityRow.TierName,
                    Link = entityRow.TierLink });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ApplicationID", PropertyValue = entityRow.ApplicationID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "TierID", PropertyValue = entityRow.TierID });
                deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                entityIdForMetricBrowser = entityRow.TierID;
            }
            else if (entityRow is EntityNode)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "EntityType", PropertyValue = "Node" });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Tier",
                    PropertyValue = entityRow.TierName,
                    Link = entityRow.TierLink
                });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Node",
                    PropertyValue = entityRow.NodeName,
                    Link = entityRow.NodeLink
                });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ApplicationID", PropertyValue = entityRow.ApplicationID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "TierID", PropertyValue = entityRow.TierID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "NodeID", PropertyValue = entityRow.NodeID });
                deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_NODE_TARGET_METRIC_ID;
                entityIdForMetricBrowser = entityRow.NodeID;
            }
            else if (entityRow is EntityBackend)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "EntityType", PropertyValue = "Backend" });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Tier",
                    PropertyValue = entityRow.TierName,
                    Link = entityRow.TierLink
                });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Backend",
                    PropertyValue = ((EntityBackend)entityRow).BackendName,
                    Link = ((EntityBackend)entityRow).BackendLink
                });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ApplicationID", PropertyValue = entityRow.ApplicationID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "TierID", PropertyValue = entityRow.TierID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "BackendID", PropertyValue = ((EntityBackend)entityRow).BackendID });
            }
            else if (entityRow is EntityBusinessTransaction)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "EntityType", PropertyValue = "Business Transaction" });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Tier",
                    PropertyValue = entityRow.TierName,
                    Link = entityRow.TierLink
                });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Business Transaction",
                    PropertyValue = ((EntityBusinessTransaction)entityRow).BTName,
                    Link = ((EntityBusinessTransaction)entityRow).BTLink
                });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ApplicationID", PropertyValue = entityRow.ApplicationID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "TierID", PropertyValue = entityRow.TierID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "BTID", PropertyValue = ((EntityBusinessTransaction)entityRow).BTID });
                deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                entityIdForMetricBrowser = entityRow.TierID;
            }
            else if (entityRow is EntityServiceEndpoint)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "EntityType", PropertyValue = "Service Endpoint" });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Tier",
                    PropertyValue = entityRow.TierName,
                    Link = entityRow.TierLink
                });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Service Endpoint",
                    PropertyValue = ((EntityServiceEndpoint)entityRow).SEPName,
                    Link = ((EntityServiceEndpoint)entityRow).SEPLink
                });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ApplicationID", PropertyValue = entityRow.ApplicationID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "TierID", PropertyValue = entityRow.TierID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "SEPID", PropertyValue = ((EntityServiceEndpoint)entityRow).SEPID });
            }
            else if (entityRow is EntityError)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "EntityType", PropertyValue = "Error" });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Tier",
                    PropertyValue = entityRow.TierName,
                    Link = entityRow.TierLink
                });
                metricSummaries.Add(new MetricSummary()
                {
                    PropertyName = "Error",
                    PropertyValue = ((EntityError)entityRow).ErrorName,
                    Link = ((EntityError)entityRow).ErrorLink
                });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ApplicationID", PropertyValue = entityRow.ApplicationID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "TierID", PropertyValue = entityRow.TierID });
                metricSummaries.Add(new MetricSummary() { PropertyName = "ErrorID", PropertyValue = ((EntityError)entityRow).ErrorID });
            }

            // Decide what kind of timerange
            string DEEPLINK_THIS_TIMERANGE = DEEPLINK_TIMERANGE_LAST_15_MINUTES;
            if (jobTimeRange != null)
            {
                long fromTimeUnix = convertToUnixTimestamp(jobTimeRange.From);
                long toTimeUnix = convertToUnixTimestamp(jobTimeRange.To);
                long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
                DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);
            }

            metricSummaries.Add(new MetricSummary()
            {
                PropertyName = "Metric ID",
                PropertyValue = metricValueObject.metricId,
                Link = String.Format(DEEPLINK_METRIC, entityRow.Controller, entityRow.ApplicationID, String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricValueObject.metricId), DEEPLINK_THIS_TIMERANGE)
            });

            // Name of the metric is always the last one in the metric path
            string[] metricPathComponents = metricValueObject.metricPath.Split('|');
            string metricName = metricPathComponents[metricPathComponents.Length - 1];
            MetricSummary metricNameMetricSummary = new MetricSummary() { PropertyName = "Metric Name", PropertyValue = metricName };
            metricSummaries.Add(metricNameMetricSummary);
            metricSummaries.Add(new MetricSummary() { PropertyName = "Metric Name (Short)", PropertyValue = metricNameToShortMetricNameMapping[metricNameMetricSummary.PropertyValue.ToString()] });
            metricSummaries.Add(new MetricSummary() { PropertyName = "Metric Name (Full)", PropertyValue = metricValueObject.metricName });
            metricSummaries.Add(new MetricSummary() { PropertyName = "Metric Path", PropertyValue = metricValueObject.metricPath });

            // Only the metrics with Average Response Time (ms) are times            
            // As long as we are not in Application Infrastructure Performance area
            if (metricName.IndexOf(METRIC_TIME_MS) > 0)
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "Rollup Type", PropertyValue = MetricType.Duration.ToString() });
            }
            else
            {
                metricSummaries.Add(new MetricSummary() { PropertyName = "Rollup Type", PropertyValue = MetricType.Count.ToString() });
            }

            return metricSummaries;
        }

        #endregion

        #region Flowmap detail conversion functions

        private static void convertFlowmapApplication(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, EntityApplication applicationRow, string metricsFolderPath)
        {
            string metricsEntityFolderPath = Path.Combine(
                metricsFolderPath,
                APPLICATION_TYPE_SHORT);

            string flowmapDataFilePath = Path.Combine(
                metricsEntityFolderPath,
                String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

            JObject flowmapData = FileIOHelper.loadJObjectFromFile(flowmapDataFilePath);
            if (flowmapData == null)
            {
                return;
            }

            long fromTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.From);
            long toTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.To);
            long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
            string DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);

            List<ActivityFlow> activityFlowsList = null;
            JArray flowmapEntities = (JArray)flowmapData["nodes"];
            JArray flowmapEntityConnections = (JArray)flowmapData["edges"];
            if (flowmapEntities != null && flowmapEntityConnections != null)
            {
                activityFlowsList = new List<ActivityFlow>(flowmapEntities.Count + flowmapEntityConnections.Count);

                // Process each of the individual Tiers, Backends and Applications as individual icons on the flow map
                foreach (JToken entity in flowmapEntities)
                {
                    ActivityFlow activityFlowRow = new ActivityFlow();
                    activityFlowRow.MetricsIDs = new List<long>(3);

                    activityFlowRow.Controller = applicationRow.Controller;
                    activityFlowRow.ApplicationName = applicationRow.ApplicationName;
                    activityFlowRow.ApplicationID = applicationRow.ApplicationID;

                    activityFlowRow.ControllerLink = String.Format(DEEPLINK_CONTROLLER, activityFlowRow.Controller, DEEPLINK_THIS_TIMERANGE);
                    activityFlowRow.ApplicationLink = String.Format(DEEPLINK_APPLICATION, activityFlowRow.Controller, activityFlowRow.ApplicationID, DEEPLINK_THIS_TIMERANGE);

                    activityFlowRow.Duration = (int)(jobConfiguration.Input.ExpandedTimeRange.To - jobConfiguration.Input.ExpandedTimeRange.From).Duration().TotalMinutes;
                    activityFlowRow.From = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime();
                    activityFlowRow.To = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime();
                    activityFlowRow.FromUtc = jobConfiguration.Input.ExpandedTimeRange.From;
                    activityFlowRow.ToUtc = jobConfiguration.Input.ExpandedTimeRange.To;

                    activityFlowRow.CallDirection = "Total";

                    activityFlowRow.FromEntityID = (long)entity["idNum"];
                    activityFlowRow.FromName = entity["name"].ToString();

                    string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
                    long entityIdForMetricBrowser = activityFlowRow.ApplicationID;
                    switch (entity["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            entityIdForMetricBrowser = activityFlowRow.FromEntityID;
                            activityFlowRow.CallType = "Total";
                            activityFlowRow.FromType = "Tier";
                            activityFlowRow.FromLink = String.Format(DEEPLINK_TIER, activityFlowRow.Controller, activityFlowRow.ApplicationID, activityFlowRow.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRow.CallType = "Total";
                            activityFlowRow.FromType = "Backend";
                            activityFlowRow.FromLink = String.Format(DEEPLINK_BACKEND, activityFlowRow.Controller, activityFlowRow.ApplicationID, activityFlowRow.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            activityFlowRow.CallType = "Total";
                            activityFlowRow.FromType = "Application";
                            activityFlowRow.FromLink = String.Format(DEEPLINK_APPLICATION, activityFlowRow.Controller, activityFlowRow.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRow.CallType = entity["entityType"].ToString();
                            activityFlowRow.FromType = "Unknown";
                            break;
                    }

                    //activityFlowRow.ToName = activityFlowRow.FromName;
                    //activityFlowRow.ToType= activityFlowRow.FromType;
                    activityFlowRow.ToEntityID = activityFlowRow.FromEntityID;
                    //activityFlowRow.ToLink = activityFlowRow.FromLink;

                    activityFlowRow.ART = (long)entity["stats"]["averageResponseTime"]["metricValue"];
                    activityFlowRow.CPM = (long)entity["stats"]["callsPerMinute"]["metricValue"];
                    activityFlowRow.EPM = (long)entity["stats"]["errorsPerMinute"]["metricValue"];
                    activityFlowRow.Calls = (long)entity["stats"]["numberOfCalls"]["metricValue"];
                    activityFlowRow.Errors = (long)entity["stats"]["numberOfErrors"]["metricValue"];

                    if (activityFlowRow.ART < 0) { activityFlowRow.ART = 0; }
                    if (activityFlowRow.CPM < 0) { activityFlowRow.ART = 0; }
                    if (activityFlowRow.EPM < 0) { activityFlowRow.EPM = 0; }
                    if (activityFlowRow.Calls < 0) { activityFlowRow.Calls = 0; }
                    if (activityFlowRow.Errors < 0) { activityFlowRow.Errors = 0; }

                    activityFlowRow.ErrorsPercentage = Math.Round((double)(double)activityFlowRow.Errors / (double)activityFlowRow.Calls * 100, 2);
                    if (Double.IsNaN(activityFlowRow.ErrorsPercentage) == true) activityFlowRow.ErrorsPercentage = 0;

                    activityFlowRow.MetricsIDs.Add((int)entity["stats"]["averageResponseTime"]["metricId"]);
                    activityFlowRow.MetricsIDs.Add((int)entity["stats"]["callsPerMinute"]["metricId"]);
                    activityFlowRow.MetricsIDs.Add((int)entity["stats"]["errorsPerMinute"]["metricId"]);
                    activityFlowRow.MetricsIDs.RemoveAll(m => m == -1);

                    if (activityFlowRow.MetricsIDs != null && activityFlowRow.MetricsIDs.Count > 0)
                    {
                        StringBuilder sb = new StringBuilder(128);
                        foreach (int metricID in activityFlowRow.MetricsIDs)
                        {
                            sb.Append(String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricID));
                            sb.Append(",");
                        }
                        sb.Remove(sb.Length - 1, 1);
                        activityFlowRow.MetricLink = String.Format(DEEPLINK_METRIC, activityFlowRow.Controller, activityFlowRow.ApplicationID, sb.ToString(), DEEPLINK_THIS_TIMERANGE);
                    }

                    activityFlowsList.Add(activityFlowRow);
                }

                // Process each call between Tiers, Tiers and Backends, and Tiers and Applications
                foreach (JToken entityConnection in flowmapEntityConnections)
                {
                    ActivityFlow activityFlowRowTemplate = new ActivityFlow();

                    // Prepare the row
                    activityFlowRowTemplate.MetricsIDs = new List<long>(3);

                    activityFlowRowTemplate.Controller = applicationRow.Controller;
                    activityFlowRowTemplate.ApplicationName = applicationRow.ApplicationName;
                    activityFlowRowTemplate.ApplicationID = applicationRow.ApplicationID;

                    activityFlowRowTemplate.ControllerLink = String.Format(DEEPLINK_CONTROLLER, activityFlowRowTemplate.Controller, DEEPLINK_THIS_TIMERANGE);
                    activityFlowRowTemplate.ApplicationLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, DEEPLINK_THIS_TIMERANGE);

                    activityFlowRowTemplate.Duration = (int)(jobConfiguration.Input.ExpandedTimeRange.To - jobConfiguration.Input.ExpandedTimeRange.From).Duration().TotalMinutes;
                    activityFlowRowTemplate.From = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime();
                    activityFlowRowTemplate.To = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime();
                    activityFlowRowTemplate.FromUtc = jobConfiguration.Input.ExpandedTimeRange.From;
                    activityFlowRowTemplate.ToUtc = jobConfiguration.Input.ExpandedTimeRange.To;

                    activityFlowRowTemplate.CallDirection = "Exit";

                    activityFlowRowTemplate.FromEntityID = (long)entityConnection["sourceNodeDefinition"]["entityId"];
                    JObject entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.FromEntityID && e["entityType"].ToString() == entityConnection["sourceNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.FromName = entity["name"].ToString();
                    }
                    string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
                    long entityIdForMetricBrowser = activityFlowRowTemplate.ApplicationID;
                    switch (entityConnection["sourceNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            entityIdForMetricBrowser = activityFlowRowTemplate.FromEntityID;
                            activityFlowRowTemplate.FromType = "Tier";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.FromType = "Backend";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            activityFlowRowTemplate.FromType = "Application";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.FromName = entityConnection["sourceNode"].ToString();
                            activityFlowRowTemplate.FromType = entityConnection["sourceNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    activityFlowRowTemplate.ToEntityID = (long)entityConnection["targetNodeDefinition"]["entityId"];
                    entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.ToEntityID && e["entityType"].ToString() == entityConnection["targetNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.ToName = entity["name"].ToString();
                    }
                    switch (entityConnection["targetNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            activityFlowRowTemplate.ToType = "Tier";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.ToType = "Backend";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            activityFlowRowTemplate.ToType = "Application";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.ToName = entityConnection["targetNode"].ToString();
                            activityFlowRowTemplate.ToType = entityConnection["targetNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    // Process each of the stats nodes, duplicating things as we need them
                    foreach (JToken entityConnectionStat in entityConnection["stats"])
                    {
                        ActivityFlow activityFlowRow = activityFlowRowTemplate.Clone();

                        activityFlowRow.CallType = entityConnectionStat["exitPointCall"]["exitPointType"].ToString();
                        if (((bool)entityConnectionStat["exitPointCall"]["synchronous"]) == false)
                        {
                            activityFlowRow.CallType = String.Format("{0} async", activityFlowRow.CallType);
                        }

                        if (entityConnectionStat["averageResponseTime"].HasValues == true)
                        {
                            activityFlowRow.ART = (long)entityConnectionStat["averageResponseTime"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["averageResponseTime"]["metricId"]);
                        }
                        if (entityConnectionStat["callsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.CPM = (long)entityConnectionStat["callsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["callsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["errorsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.EPM = (long)entityConnectionStat["errorsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["errorsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["numberOfCalls"].HasValues == true) { activityFlowRow.Calls = (long)entityConnectionStat["numberOfCalls"]["metricValue"]; }
                        if (entityConnectionStat["numberOfErrors"].HasValues == true) { activityFlowRow.Errors = (long)entityConnectionStat["numberOfErrors"]["metricValue"]; }

                        if (activityFlowRow.ART < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.CPM < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.EPM < 0) { activityFlowRow.EPM = 0; }
                        if (activityFlowRow.Calls < 0) { activityFlowRow.Calls = 0; }
                        if (activityFlowRow.Errors < 0) { activityFlowRow.Errors = 0; }

                        activityFlowRow.ErrorsPercentage = Math.Round((double)(double)activityFlowRow.Errors / (double)activityFlowRow.Calls * 100, 2);
                        if (Double.IsNaN(activityFlowRow.ErrorsPercentage) == true) activityFlowRow.ErrorsPercentage = 0;
                        
                        activityFlowRow.MetricsIDs.RemoveAll(m => m == -1);

                        if (activityFlowRow.MetricsIDs != null && activityFlowRow.MetricsIDs.Count > 0)
                        {
                            StringBuilder sb = new StringBuilder(128);
                            foreach (int metricID in activityFlowRow.MetricsIDs)
                            {
                                sb.Append(String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricID));
                                sb.Append(",");
                            }
                            sb.Remove(sb.Length - 1, 1);
                            activityFlowRow.MetricLink = String.Format(DEEPLINK_METRIC, activityFlowRow.Controller, activityFlowRow.ApplicationID, sb.ToString(), DEEPLINK_THIS_TIMERANGE);
                        }
                        activityFlowsList.Add(activityFlowRow);
                    }
                }
            }

            // Sort them
            activityFlowsList = activityFlowsList.OrderBy(a => a.CallDirection).ThenBy(a => a.FromType).ThenBy(a => a.FromName).ThenBy(a => a.ToName).ThenBy(a => a.CallType).ThenBy(a => a.CPM).ToList();

            string activityGridReportFileName = Path.Combine(
                metricsEntityFolderPath,
                CONVERT_ACTIVITY_GRID_FILE_NAME);

            FileIOHelper.writeListToCSVFile(activityFlowsList, new ActivityFlowReportMap(), activityGridReportFileName);

            return;
        }

        private static void convertFlowmapTier(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, EntityTier tierRow, string metricsFolderPath)
        {
            string metricsEntityFolderPath = Path.Combine(
                metricsFolderPath,
                TIERS_TYPE_SHORT,
                getShortenedEntityNameForFileSystem(tierRow.TierName, tierRow.TierID));

            string flowmapDataFilePath = Path.Combine(
                metricsEntityFolderPath,
                String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

            JObject flowmapData = FileIOHelper.loadJObjectFromFile(flowmapDataFilePath);
            if (flowmapData == null)
            {
                return;
            }

            long fromTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.From);
            long toTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.To);
            long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
            string DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);

            List<ActivityFlow> activityFlowsList = null;
            JArray flowmapEntities = (JArray)flowmapData["nodes"];
            JArray flowmapEntityConnections = (JArray)flowmapData["edges"];
            if (flowmapEntities != null && flowmapEntityConnections != null)
            {
                activityFlowsList = new List<ActivityFlow>(flowmapEntityConnections.Count);

                // For Tiers, not going to process individual entities, but only connecting lines

                // Process each call between Tiers, Tiers and Backends, and Tiers and Applications
                foreach (JToken entityConnection in flowmapEntityConnections)
                {
                    ActivityFlow activityFlowRowTemplate = new ActivityFlow();

                    // Prepare the row
                    activityFlowRowTemplate.MetricsIDs = new List<long>(3);

                    activityFlowRowTemplate.Controller = tierRow.Controller;
                    activityFlowRowTemplate.ApplicationName = tierRow.ApplicationName;
                    activityFlowRowTemplate.ApplicationID = tierRow.ApplicationID;

                    activityFlowRowTemplate.ControllerLink = String.Format(DEEPLINK_CONTROLLER, activityFlowRowTemplate.Controller, DEEPLINK_THIS_TIMERANGE);
                    activityFlowRowTemplate.ApplicationLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, DEEPLINK_THIS_TIMERANGE);

                    activityFlowRowTemplate.Duration = (int)(jobConfiguration.Input.ExpandedTimeRange.To - jobConfiguration.Input.ExpandedTimeRange.From).Duration().TotalMinutes;
                    activityFlowRowTemplate.From = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime();
                    activityFlowRowTemplate.To = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime();
                    activityFlowRowTemplate.FromUtc = jobConfiguration.Input.ExpandedTimeRange.From;
                    activityFlowRowTemplate.ToUtc = jobConfiguration.Input.ExpandedTimeRange.To;

                    activityFlowRowTemplate.FromEntityID = (long)entityConnection["sourceNodeDefinition"]["entityId"];
                    JObject entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.FromEntityID && e["entityType"].ToString() == entityConnection["sourceNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.FromName = entity["name"].ToString();
                    }
                    string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
                    long entityIdForMetricBrowser = activityFlowRowTemplate.ApplicationID;
                    switch (entityConnection["sourceNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            entityIdForMetricBrowser = activityFlowRowTemplate.FromEntityID;
                            activityFlowRowTemplate.FromType = "Tier";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.FromType = "Backend";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            activityFlowRowTemplate.FromType = "Application";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.FromName = entityConnection["sourceNode"].ToString();
                            activityFlowRowTemplate.FromType = entityConnection["sourceNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    activityFlowRowTemplate.ToEntityID = (long)entityConnection["targetNodeDefinition"]["entityId"];
                    entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.ToEntityID && e["entityType"].ToString() == entityConnection["targetNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.ToName = entity["name"].ToString();
                    }
                    switch (entityConnection["targetNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            activityFlowRowTemplate.ToType = "Tier";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.ToType = "Backend";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            activityFlowRowTemplate.ToType = "Application";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.ToName = entityConnection["targetNode"].ToString();
                            activityFlowRowTemplate.ToType = entityConnection["targetNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    if (activityFlowRowTemplate.FromEntityID == tierRow.TierID)
                    {
                        activityFlowRowTemplate.CallDirection = "Outgoing";
                    }
                    else
                    {
                        activityFlowRowTemplate.CallDirection = "Incoming";
                    }

                    // Process each of the stats nodes, duplicating things as we need them
                    foreach (JToken entityConnectionStat in entityConnection["stats"])
                    {
                        ActivityFlow activityFlowRow = activityFlowRowTemplate.Clone();

                        activityFlowRow.CallType = entityConnectionStat["exitPointCall"]["exitPointType"].ToString();
                        if (((bool)entityConnectionStat["exitPointCall"]["synchronous"]) == false)
                        {
                            activityFlowRow.CallType = String.Format("{0} async", activityFlowRow.CallType);
                        }

                        if (entityConnectionStat["averageResponseTime"].HasValues == true)
                        {
                            activityFlowRow.ART = (long)entityConnectionStat["averageResponseTime"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["averageResponseTime"]["metricId"]);
                        }
                        if (entityConnectionStat["callsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.CPM = (long)entityConnectionStat["callsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["callsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["errorsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.EPM = (long)entityConnectionStat["errorsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["errorsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["numberOfCalls"].HasValues == true) { activityFlowRow.Calls = (long)entityConnectionStat["numberOfCalls"]["metricValue"]; }
                        if (entityConnectionStat["numberOfErrors"].HasValues == true) { activityFlowRow.Errors = (long)entityConnectionStat["numberOfErrors"]["metricValue"]; }

                        if (activityFlowRow.ART < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.CPM < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.EPM < 0) { activityFlowRow.EPM = 0; }
                        if (activityFlowRow.Calls < 0) { activityFlowRow.Calls = 0; }
                        if (activityFlowRow.Errors < 0) { activityFlowRow.Errors = 0; }

                        activityFlowRow.ErrorsPercentage = Math.Round((double)(double)activityFlowRow.Errors / (double)activityFlowRow.Calls * 100, 2);
                        if (Double.IsNaN(activityFlowRow.ErrorsPercentage) == true) activityFlowRow.ErrorsPercentage = 0;

                        activityFlowRow.MetricsIDs.RemoveAll(m => m == -1);

                        if (activityFlowRow.MetricsIDs != null && activityFlowRow.MetricsIDs.Count > 0)
                        {
                            StringBuilder sb = new StringBuilder(128);
                            foreach (int metricID in activityFlowRow.MetricsIDs)
                            {
                                sb.Append(String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricID));
                                sb.Append(",");
                            }
                            sb.Remove(sb.Length - 1, 1);
                            activityFlowRow.MetricLink = String.Format(DEEPLINK_METRIC, activityFlowRow.Controller, activityFlowRow.ApplicationID, sb.ToString(), DEEPLINK_THIS_TIMERANGE);
                        }
                        activityFlowsList.Add(activityFlowRow);
                    }
                }
            }

            // Sort them
            activityFlowsList = activityFlowsList.OrderBy(a => a.CallDirection).ThenBy(a => a.FromType).ThenBy(a => a.FromName).ThenBy(a => a.ToType).ThenBy(a => a.ToName).ThenBy(a => a.CallType).ThenBy(a => a.CPM).ToList();

            string activityGridReportFileName = Path.Combine(
                metricsEntityFolderPath,
                CONVERT_ACTIVITY_GRID_FILE_NAME);

            FileIOHelper.writeListToCSVFile(activityFlowsList, new ActivityFlowReportMap(), activityGridReportFileName);
        }

        private static void convertFlowmapNode(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, EntityNode nodeRow, string metricsFolderPath)
        {
            string metricsEntityFolderPath = Path.Combine(
                metricsFolderPath,
                NODES_TYPE_SHORT,
                getShortenedEntityNameForFileSystem(nodeRow.TierName, nodeRow.TierID),
                getShortenedEntityNameForFileSystem(nodeRow.NodeName, nodeRow.NodeID));

            string flowmapDataFilePath = Path.Combine(
                metricsEntityFolderPath,
                String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

            JObject flowmapData = FileIOHelper.loadJObjectFromFile(flowmapDataFilePath);
            if (flowmapData == null)
            {
                return;
            }

            long fromTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.From);
            long toTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.To);
            long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
            string DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);

            List<ActivityFlow> activityFlowsList = null;
            JArray flowmapEntities = (JArray)flowmapData["nodes"];
            JArray flowmapEntityConnections = (JArray)flowmapData["edges"];
            if (flowmapEntities != null && flowmapEntityConnections != null)
            {
                activityFlowsList = new List<ActivityFlow>(flowmapEntityConnections.Count);

                // For Nodes, not going to process individual entities, but only connecting lines

                // Process each call between Tiers, Tiers and Backends, and Tiers and Applications
                foreach (JToken entityConnection in flowmapEntityConnections)
                {
                    ActivityFlow activityFlowRowTemplate = new ActivityFlow();

                    // Prepare the row
                    activityFlowRowTemplate.MetricsIDs = new List<long>(3);

                    activityFlowRowTemplate.Controller = nodeRow.Controller;
                    activityFlowRowTemplate.ApplicationName = nodeRow.ApplicationName;
                    activityFlowRowTemplate.ApplicationID = nodeRow.ApplicationID;

                    activityFlowRowTemplate.ControllerLink = String.Format(DEEPLINK_CONTROLLER, activityFlowRowTemplate.Controller, DEEPLINK_THIS_TIMERANGE);
                    activityFlowRowTemplate.ApplicationLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, DEEPLINK_THIS_TIMERANGE);

                    activityFlowRowTemplate.Duration = (int)(jobConfiguration.Input.ExpandedTimeRange.To - jobConfiguration.Input.ExpandedTimeRange.From).Duration().TotalMinutes;
                    activityFlowRowTemplate.From = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime();
                    activityFlowRowTemplate.To = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime();
                    activityFlowRowTemplate.FromUtc = jobConfiguration.Input.ExpandedTimeRange.From;
                    activityFlowRowTemplate.ToUtc = jobConfiguration.Input.ExpandedTimeRange.To;

                    activityFlowRowTemplate.FromEntityID = (long)entityConnection["sourceNodeDefinition"]["entityId"];
                    JObject entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.FromEntityID && e["entityType"].ToString() == entityConnection["sourceNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.FromName = entity["name"].ToString();
                    }
                    string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
                    long entityIdForMetricBrowser = activityFlowRowTemplate.ApplicationID;
                    switch (entityConnection["sourceNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_NODE:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_NODE_TARGET_METRIC_ID;
                            entityIdForMetricBrowser = activityFlowRowTemplate.FromEntityID;
                            activityFlowRowTemplate.FromType = "Node";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_NODE, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_TIER:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            entityIdForMetricBrowser = activityFlowRowTemplate.FromEntityID;
                            activityFlowRowTemplate.FromType = "Tier";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.FromType = "Backend";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            activityFlowRowTemplate.FromType = "Application";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.FromName = entityConnection["sourceNode"].ToString();
                            activityFlowRowTemplate.FromType = entityConnection["sourceNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    activityFlowRowTemplate.ToEntityID = (long)entityConnection["targetNodeDefinition"]["entityId"];
                    entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.ToEntityID && e["entityType"].ToString() == entityConnection["targetNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.ToName = entity["name"].ToString();
                    }
                    switch (entityConnection["targetNodeDefinition"]["entityType"].ToString())
                    {
                        case "APPLICATION_COMPONENT_NODE":
                            activityFlowRowTemplate.ToType = "Node";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_NODE, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_TIER:
                            activityFlowRowTemplate.ToType = "Tier";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.ToType = "Backend";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            activityFlowRowTemplate.ToType = "Application";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.ToName = entityConnection["targetNode"].ToString();
                            activityFlowRowTemplate.ToType = entityConnection["targetNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    // Haven't seen the incoming calls on the flowmap for Nodes. But maybe?
                    if (activityFlowRowTemplate.FromEntityID == nodeRow.NodeID)
                    {
                        activityFlowRowTemplate.CallDirection = "Outgoing";
                    }
                    else
                    {
                        activityFlowRowTemplate.CallDirection = "Incoming";
                    }

                    // Process each of the stats nodes, duplicating things as we need them
                    foreach (JToken entityConnectionStat in entityConnection["stats"])
                    {
                        ActivityFlow activityFlowRow = activityFlowRowTemplate.Clone();

                        activityFlowRow.CallType = entityConnectionStat["exitPointCall"]["exitPointType"].ToString();
                        if (((bool)entityConnectionStat["exitPointCall"]["synchronous"]) == false)
                        {
                            activityFlowRow.CallType = String.Format("{0} async", activityFlowRow.CallType);
                        }

                        if (entityConnectionStat["averageResponseTime"].HasValues == true)
                        {
                            activityFlowRow.ART = (long)entityConnectionStat["averageResponseTime"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["averageResponseTime"]["metricId"]);
                        }
                        if (entityConnectionStat["callsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.CPM = (long)entityConnectionStat["callsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["callsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["errorsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.EPM = (long)entityConnectionStat["errorsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["errorsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["numberOfCalls"].HasValues == true) { activityFlowRow.Calls = (long)entityConnectionStat["numberOfCalls"]["metricValue"]; }
                        if (entityConnectionStat["numberOfErrors"].HasValues == true) { activityFlowRow.Errors = (long)entityConnectionStat["numberOfErrors"]["metricValue"]; }

                        if (activityFlowRow.ART < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.CPM < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.EPM < 0) { activityFlowRow.EPM = 0; }
                        if (activityFlowRow.Calls < 0) { activityFlowRow.Calls = 0; }
                        if (activityFlowRow.Errors < 0) { activityFlowRow.Errors = 0; }

                        activityFlowRow.ErrorsPercentage = Math.Round((double)(double)activityFlowRow.Errors / (double)activityFlowRow.Calls * 100, 2);
                        if (Double.IsNaN(activityFlowRow.ErrorsPercentage) == true) activityFlowRow.ErrorsPercentage = 0;

                        activityFlowRow.MetricsIDs.RemoveAll(m => m == -1);

                        if (activityFlowRow.MetricsIDs != null && activityFlowRow.MetricsIDs.Count > 0)
                        {
                            StringBuilder sb = new StringBuilder(128);
                            foreach (int metricID in activityFlowRow.MetricsIDs)
                            {
                                sb.Append(String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricID));
                                sb.Append(",");
                            }
                            sb.Remove(sb.Length - 1, 1);
                            activityFlowRow.MetricLink = String.Format(DEEPLINK_METRIC, activityFlowRow.Controller, activityFlowRow.ApplicationID, sb.ToString(), DEEPLINK_THIS_TIMERANGE);
                        }
                        activityFlowsList.Add(activityFlowRow);
                    }
                }
            }

            // Sort them
            activityFlowsList = activityFlowsList.OrderBy(a => a.CallDirection).ThenBy(a => a.FromType).ThenBy(a => a.FromName).ThenBy(a => a.ToType).ThenBy(a => a.ToName).ThenBy(a => a.CallType).ThenBy(a => a.CPM).ToList();

            string activityGridReportFileName = Path.Combine(
                metricsEntityFolderPath,
                CONVERT_ACTIVITY_GRID_FILE_NAME);

            FileIOHelper.writeListToCSVFile(activityFlowsList, new ActivityFlowReportMap(), activityGridReportFileName);
        }

        private static void convertFlowmapsBusinessTransaction(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, EntityBusinessTransaction businessTransactionRow, string metricsFolderPath)
        {
            string metricsEntityFolderPath = Path.Combine(
                metricsFolderPath,
                BUSINESS_TRANSACTIONS_TYPE_SHORT,
                getShortenedEntityNameForFileSystem(businessTransactionRow.TierName, businessTransactionRow.TierID),
                getShortenedEntityNameForFileSystem(businessTransactionRow.BTName, businessTransactionRow.BTID));

            string flowmapDataFilePath = Path.Combine(
                metricsEntityFolderPath,
                String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

            JObject flowmapData = FileIOHelper.loadJObjectFromFile(flowmapDataFilePath);
            if (flowmapData == null)
            {
                return;
            }

            long fromTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.From);
            long toTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.To);
            long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
            string DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);

            List<ActivityFlow> activityFlowsList = null;
            JArray flowmapEntities = (JArray)flowmapData["nodes"];
            JArray flowmapEntityConnections = (JArray)flowmapData["edges"];
            if (flowmapEntities != null && flowmapEntityConnections != null)
            {
                activityFlowsList = new List<ActivityFlow>(flowmapEntityConnections.Count);

                // Controller shows a pretty complex grid view for jumps that continue from other tiers.
                // I couldn't figure out how the JSON is converted into that
                // For Business Transactions, not going to process individual entities, but only connecting lines

                // Assume that the first node is the 
                JObject startTier = (JObject)flowmapEntities.Where(e => (bool)e["startComponent"] == true).FirstOrDefault();

                // Process each call between Tiers, Tiers and Backends, and Tiers and Applications
                foreach (JToken entityConnection in flowmapEntityConnections)
                {
                    ActivityFlow activityFlowRowTemplate = new ActivityFlow();

                    // Prepare the row
                    activityFlowRowTemplate.MetricsIDs = new List<long>(3);

                    activityFlowRowTemplate.Controller = businessTransactionRow.Controller;
                    activityFlowRowTemplate.ApplicationName = businessTransactionRow.ApplicationName;
                    activityFlowRowTemplate.ApplicationID = businessTransactionRow.ApplicationID;

                    activityFlowRowTemplate.ControllerLink = String.Format(DEEPLINK_CONTROLLER, activityFlowRowTemplate.Controller, DEEPLINK_THIS_TIMERANGE);
                    activityFlowRowTemplate.ApplicationLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, DEEPLINK_THIS_TIMERANGE);

                    activityFlowRowTemplate.Duration = (int)(jobConfiguration.Input.ExpandedTimeRange.To - jobConfiguration.Input.ExpandedTimeRange.From).Duration().TotalMinutes;
                    activityFlowRowTemplate.From = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime();
                    activityFlowRowTemplate.To = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime();
                    activityFlowRowTemplate.FromUtc = jobConfiguration.Input.ExpandedTimeRange.From;
                    activityFlowRowTemplate.ToUtc = jobConfiguration.Input.ExpandedTimeRange.To;

                    activityFlowRowTemplate.FromEntityID = (long)entityConnection["sourceNodeDefinition"]["entityId"];
                    JObject entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.FromEntityID && e["entityType"].ToString() == entityConnection["sourceNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.FromName = entity["name"].ToString();
                    }
                    string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
                    long entityIdForMetricBrowser = activityFlowRowTemplate.ApplicationID;
                    switch (entityConnection["sourceNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            entityIdForMetricBrowser = activityFlowRowTemplate.FromEntityID;
                            activityFlowRowTemplate.FromType = "Tier";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.FromType = "Backend";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            activityFlowRowTemplate.FromType = "Application";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.FromName = entityConnection["sourceNode"].ToString();
                            activityFlowRowTemplate.FromType = entityConnection["sourceNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    activityFlowRowTemplate.ToEntityID = (long)entityConnection["targetNodeDefinition"]["entityId"];
                    entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.ToEntityID && e["entityType"].ToString() == entityConnection["targetNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.ToName = entity["name"].ToString();
                    }
                    switch (entityConnection["targetNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            activityFlowRowTemplate.ToType = "Tier";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.ToType = "Backend";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            activityFlowRowTemplate.ToType = "Application";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.ToName = entityConnection["targetNode"].ToString();
                            activityFlowRowTemplate.ToType = entityConnection["targetNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    // Haven't seen the incoming calls on the flowmap for Nodes. But maybe?
                    if (startTier != null)
                    {
                        if (activityFlowRowTemplate.FromEntityID == (long)startTier["idNum"])
                        {
                            activityFlowRowTemplate.CallDirection = "FirstHop";
                        }
                        else
                        {
                            activityFlowRowTemplate.CallDirection = "SubsequentHop";
                        }
                    }

                    // Process each of the stats nodes, duplicating things as we need them
                    foreach (JToken entityConnectionStat in entityConnection["stats"])
                    {
                        ActivityFlow activityFlowRow = activityFlowRowTemplate.Clone();

                        activityFlowRow.CallType = entityConnectionStat["exitPointCall"]["exitPointType"].ToString();
                        if (((bool)entityConnectionStat["exitPointCall"]["synchronous"]) == false)
                        {
                            activityFlowRow.CallType = String.Format("{0} async", activityFlowRow.CallType);
                        }

                        if (entityConnectionStat["averageResponseTime"].HasValues == true)
                        {
                            activityFlowRow.ART = (long)entityConnectionStat["averageResponseTime"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["averageResponseTime"]["metricId"]);
                        }
                        if (entityConnectionStat["callsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.CPM = (long)entityConnectionStat["callsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["callsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["errorsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.EPM = (long)entityConnectionStat["errorsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["errorsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["numberOfCalls"].HasValues == true) { activityFlowRow.Calls = (long)entityConnectionStat["numberOfCalls"]["metricValue"]; }
                        if (entityConnectionStat["numberOfErrors"].HasValues == true) { activityFlowRow.Errors = (long)entityConnectionStat["numberOfErrors"]["metricValue"]; }

                        if (activityFlowRow.ART < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.CPM < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.EPM < 0) { activityFlowRow.EPM = 0; }
                        if (activityFlowRow.Calls < 0) { activityFlowRow.Calls = 0; }
                        if (activityFlowRow.Errors < 0) { activityFlowRow.Errors = 0; }

                        activityFlowRow.ErrorsPercentage = Math.Round((double)(double)activityFlowRow.Errors / (double)activityFlowRow.Calls * 100, 2);
                        if (Double.IsNaN(activityFlowRow.ErrorsPercentage) == true) activityFlowRow.ErrorsPercentage = 0;

                        activityFlowRow.MetricsIDs.RemoveAll(m => m == -1);

                        if (activityFlowRow.MetricsIDs != null && activityFlowRow.MetricsIDs.Count > 0)
                        {
                            StringBuilder sb = new StringBuilder(128);
                            foreach (int metricID in activityFlowRow.MetricsIDs)
                            {
                                sb.Append(String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricID));
                                sb.Append(",");
                            }
                            sb.Remove(sb.Length - 1, 1);
                            activityFlowRow.MetricLink = String.Format(DEEPLINK_METRIC, activityFlowRow.Controller, activityFlowRow.ApplicationID, sb.ToString(), DEEPLINK_THIS_TIMERANGE);
                        }
                        activityFlowsList.Add(activityFlowRow);
                    }
                }
            }

            // Sort them
            activityFlowsList = activityFlowsList.OrderBy(a => a.CallDirection).ThenBy(a => a.FromType).ThenBy(a => a.FromName).ThenBy(a => a.ToType).ThenBy(a => a.ToName).ThenBy(a => a.CallType).ThenBy(a => a.CPM).ToList();

            string activityGridReportFileName = Path.Combine(
                metricsEntityFolderPath,
                CONVERT_ACTIVITY_GRID_FILE_NAME);

            FileIOHelper.writeListToCSVFile(activityFlowsList, new ActivityFlowReportMap(), activityGridReportFileName);
        }

        private static void convertFlowmapBackend(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, EntityBackend backendRow, string metricsFolderPath)
        {
            string metricsEntityFolderPath = Path.Combine(
                metricsFolderPath,
                BACKENDS_TYPE_SHORT,
                getShortenedEntityNameForFileSystem(backendRow.BackendName, backendRow.BackendID));

            string flowmapDataFilePath = Path.Combine(
                metricsEntityFolderPath,
                String.Format(EXTRACT_ENTITY_FLOWMAP_FILE_NAME, jobConfiguration.Input.ExpandedTimeRange.From, jobConfiguration.Input.ExpandedTimeRange.To));

            JObject flowmapData = FileIOHelper.loadJObjectFromFile(flowmapDataFilePath);
            if (flowmapData == null)
            {
                return;
            }

            long fromTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.From);
            long toTimeUnix = convertToUnixTimestamp(jobConfiguration.Input.ExpandedTimeRange.To);
            long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
            string DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);

            List<ActivityFlow> activityFlowsList = null;
            JArray flowmapEntities = (JArray)flowmapData["nodes"];
            JArray flowmapEntityConnections = (JArray)flowmapData["edges"];
            if (flowmapEntities != null && flowmapEntityConnections != null)
            {
                activityFlowsList = new List<ActivityFlow>(flowmapEntityConnections.Count);

                // We don't display grid for Backends. But it is quite similar to Tier view
                // For Backends, not going to process individual entities, but only connecting lines

                // Process each call between Tiers, Tiers and Backends
                foreach (JToken entityConnection in flowmapEntityConnections)
                {
                    ActivityFlow activityFlowRowTemplate = new ActivityFlow();

                    // Prepare the row
                    activityFlowRowTemplate.MetricsIDs = new List<long>(3);

                    activityFlowRowTemplate.Controller = backendRow.Controller;
                    activityFlowRowTemplate.ApplicationName = backendRow.ApplicationName;
                    activityFlowRowTemplate.ApplicationID = backendRow.ApplicationID;

                    activityFlowRowTemplate.ControllerLink = String.Format(DEEPLINK_CONTROLLER, activityFlowRowTemplate.Controller, DEEPLINK_THIS_TIMERANGE);
                    activityFlowRowTemplate.ApplicationLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, DEEPLINK_THIS_TIMERANGE);

                    activityFlowRowTemplate.Duration = (int)(jobConfiguration.Input.ExpandedTimeRange.To - jobConfiguration.Input.ExpandedTimeRange.From).Duration().TotalMinutes;
                    activityFlowRowTemplate.From = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime();
                    activityFlowRowTemplate.To = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime();
                    activityFlowRowTemplate.FromUtc = jobConfiguration.Input.ExpandedTimeRange.From;
                    activityFlowRowTemplate.ToUtc = jobConfiguration.Input.ExpandedTimeRange.To;

                    activityFlowRowTemplate.FromEntityID = (long)entityConnection["sourceNodeDefinition"]["entityId"];
                    JObject entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.FromEntityID && e["entityType"].ToString() == entityConnection["sourceNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.FromName = entity["name"].ToString();
                    }
                    string deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_APPLICATION_TARGET_METRIC_ID;
                    long entityIdForMetricBrowser = activityFlowRowTemplate.ApplicationID;
                    switch (entityConnection["sourceNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            entityIdForMetricBrowser = activityFlowRowTemplate.FromEntityID;
                            activityFlowRowTemplate.FromType = "Tier";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.FromType = "Backend";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            deepLinkMetricTemplateInMetricBrowser = DEEPLINK_METRIC_TIER_TARGET_METRIC_ID;
                            activityFlowRowTemplate.FromType = "Application";
                            activityFlowRowTemplate.FromLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.FromEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.FromName = entityConnection["sourceNode"].ToString();
                            activityFlowRowTemplate.FromType = entityConnection["sourceNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    activityFlowRowTemplate.ToEntityID = (long)entityConnection["targetNodeDefinition"]["entityId"];
                    entity = (JObject)flowmapEntities.Where(e => (long)e["idNum"] == activityFlowRowTemplate.ToEntityID && e["entityType"].ToString() == entityConnection["targetNodeDefinition"]["entityType"].ToString()).FirstOrDefault();
                    if (entity != null)
                    {
                        activityFlowRowTemplate.ToName = entity["name"].ToString();
                    }
                    switch (entityConnection["targetNodeDefinition"]["entityType"].ToString())
                    {
                        case ENTITY_TYPE_TIER:
                            activityFlowRowTemplate.ToType = "Tier";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_TIER, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_BACKEND:
                            activityFlowRowTemplate.ToType = "Backend";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_BACKEND, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ApplicationID, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        case ENTITY_TYPE_APPLICATION:
                            activityFlowRowTemplate.ToType = "Application";
                            activityFlowRowTemplate.ToLink = String.Format(DEEPLINK_APPLICATION, activityFlowRowTemplate.Controller, activityFlowRowTemplate.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                            break;

                        default:
                            activityFlowRowTemplate.ToName = entityConnection["targetNode"].ToString();
                            activityFlowRowTemplate.ToType = entityConnection["targetNodeDefinition"]["entityType"].ToString();
                            break;
                    }

                    if (activityFlowRowTemplate.FromEntityID == backendRow.BackendID)
                    {
                        activityFlowRowTemplate.CallDirection = "Outgoing";
                    }
                    else
                    {
                        activityFlowRowTemplate.CallDirection = "Incoming";
                    }

                    // Process each of the stats nodes, duplicating things as we need them
                    foreach (JToken entityConnectionStat in entityConnection["stats"])
                    {
                        ActivityFlow activityFlowRow = activityFlowRowTemplate.Clone();

                        activityFlowRow.CallType = entityConnectionStat["exitPointCall"]["exitPointType"].ToString();
                        if (((bool)entityConnectionStat["exitPointCall"]["synchronous"]) == false)
                        {
                            activityFlowRow.CallType = String.Format("{0} async", activityFlowRow.CallType);
                        }

                        if (entityConnectionStat["averageResponseTime"].HasValues == true)
                        {
                            activityFlowRow.ART = (long)entityConnectionStat["averageResponseTime"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["averageResponseTime"]["metricId"]);
                        }
                        if (entityConnectionStat["callsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.CPM = (long)entityConnectionStat["callsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["callsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["errorsPerMinute"].HasValues == true)
                        {
                            activityFlowRow.EPM = (long)entityConnectionStat["errorsPerMinute"]["metricValue"];
                            activityFlowRow.MetricsIDs.Add((long)entityConnectionStat["errorsPerMinute"]["metricId"]);
                        }
                        if (entityConnectionStat["numberOfCalls"].HasValues == true) { activityFlowRow.Calls = (long)entityConnectionStat["numberOfCalls"]["metricValue"]; }
                        if (entityConnectionStat["numberOfErrors"].HasValues == true) { activityFlowRow.Errors = (long)entityConnectionStat["numberOfErrors"]["metricValue"]; }

                        if (activityFlowRow.ART < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.CPM < 0) { activityFlowRow.ART = 0; }
                        if (activityFlowRow.EPM < 0) { activityFlowRow.EPM = 0; }
                        if (activityFlowRow.Calls < 0) { activityFlowRow.Calls = 0; }
                        if (activityFlowRow.Errors < 0) { activityFlowRow.Errors = 0; }

                        activityFlowRow.ErrorsPercentage = Math.Round((double)(double)activityFlowRow.Errors / (double)activityFlowRow.Calls * 100, 2);
                        if (Double.IsNaN(activityFlowRow.ErrorsPercentage) == true) activityFlowRow.ErrorsPercentage = 0;

                        activityFlowRow.MetricsIDs.RemoveAll(m => m == -1);

                        if (activityFlowRow.MetricsIDs != null && activityFlowRow.MetricsIDs.Count > 0)
                        {
                            StringBuilder sb = new StringBuilder(128);
                            foreach (int metricID in activityFlowRow.MetricsIDs)
                            {
                                sb.Append(String.Format(deepLinkMetricTemplateInMetricBrowser, entityIdForMetricBrowser, metricID));
                                sb.Append(",");
                            }
                            sb.Remove(sb.Length - 1, 1);
                            activityFlowRow.MetricLink = String.Format(DEEPLINK_METRIC, activityFlowRow.Controller, activityFlowRow.ApplicationID, sb.ToString(), DEEPLINK_THIS_TIMERANGE);
                        }
                        activityFlowsList.Add(activityFlowRow);
                    }
                }
            }

            // Sort them
            activityFlowsList = activityFlowsList.OrderBy(a => a.CallDirection).ThenBy(a => a.FromType).ThenBy(a => a.FromName).ThenBy(a => a.ToType).ThenBy(a => a.ToName).ThenBy(a => a.CallType).ThenBy(a => a.CPM).ToList();

            string activityGridReportFileName = Path.Combine(
                metricsEntityFolderPath,
                CONVERT_ACTIVITY_GRID_FILE_NAME);

            FileIOHelper.writeListToCSVFile(activityFlowsList, new ActivityFlowReportMap(), activityGridReportFileName);
        }

        #endregion

        #region Snapshot conversion functions

        private static int indexSnapshots(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, JobTimeRange jobTimeRange, List<JToken> entityList, List<EntityTier> tiersList, List<EntityBackend> backendsList, List<EntityServiceEndpoint> serviceEndpointsList, List<EntityError> errorsList, bool progressToConsole)
        {
            int j = 0;

            #region Target step variables

            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string snapshotsFolderPath = Path.Combine(applicationFolderPath, SNAPSHOTS_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            foreach (JToken snapshotToken in entityList)
            {
                // Only do first in chain
                if ((bool)snapshotToken["firstInChain"] == false)
                {
                    continue;
                }

                logger.Info("Indexing snapshot for Application {0}, Tier {1}, Business Transaction {2}, RequestGUID {3}", jobTarget.Application, snapshotToken["applicationComponentName"], snapshotToken["businessTransactionName"], snapshotToken["requestGUID"]);

                #region Target step variables

                DateTime snapshotTime = convertFromUnixTimestamp((long)snapshotToken["serverStartTime"]);

                string snapshotFolderPath = Path.Combine(
                    snapshotsFolderPath,
                    getShortenedEntityNameForFileSystem(snapshotToken["applicationComponentName"].ToString(), (long)snapshotToken["applicationComponentId"]),
                    getShortenedEntityNameForFileSystem(snapshotToken["businessTransactionName"].ToString(), (long)snapshotToken["businessTransactionId"]),
                    String.Format("{0:yyyyMMddHH}", snapshotTime),
                    userExperienceFolderNameMapping[snapshotToken["userExperience"].ToString()],
                    String.Format(SNAPSHOT_FOLDER_NAME, snapshotToken["requestGUID"], snapshotTime));

                string snapshotSegmentsDataFilePath = Path.Combine(snapshotFolderPath, EXTRACT_SNAPSHOT_SEGMENT_FILE_NAME);

                string snapshotsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_FILE_NAME);
                string segmentsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_FILE_NAME);
                string exitCallsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_EXIT_CALLS_FILE_NAME);
                string serviceEndpointCallsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_SERVICE_ENDPOINT_CALLS_FILE_NAME);
                string detectedErrorsFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_DETECTED_ERRORS_FILE_NAME);
                string businessDataFileName = Path.Combine(snapshotFolderPath, CONVERT_SNAPSHOT_SEGMENTS_BUSINESS_DATA_FILE_NAME);

                #endregion

                #region Fill in Snapshot data

                Snapshot snapshot = new Snapshot();
                snapshot.Controller = jobTarget.Controller;
                snapshot.ApplicationName = jobTarget.Application;
                snapshot.ApplicationID = jobTarget.ApplicationID;
                snapshot.TierID = (long)snapshotToken["applicationComponentId"];
                snapshot.TierName = snapshotToken["applicationComponentName"].ToString();
                snapshot.BTID = (long)snapshotToken["businessTransactionId"];
                snapshot.BTName = snapshotToken["businessTransactionName"].ToString();
                snapshot.NodeID = (long)snapshotToken["applicationComponentNodeId"];
                snapshot.NodeName = snapshotToken["applicationComponentNodeName"].ToString();

                snapshot.OccuredUtc = convertFromUnixTimestamp((long)snapshotToken["serverStartTime"]);
                snapshot.Occured = snapshot.OccuredUtc.ToLocalTime();

                snapshot.RequestID = snapshotToken["requestGUID"].ToString();
                snapshot.UserExperience = snapshotToken["userExperience"].ToString();
                snapshot.Duration = (long)snapshotToken["timeTakenInMilliSecs"];
                snapshot.DiagSessionID = snapshotToken["diagnosticSessionGUID"].ToString();
                if (snapshotToken["url"] != null) { snapshot.URL = snapshotToken["url"].ToString(); }

                snapshot.TakenSummary = snapshotToken["summary"].ToString();
                if (snapshot.TakenSummary.Contains("Scheduled Snapshots:") == true)
                {
                    snapshot.TakenReason = "Scheduled";
                }
                else if (snapshot.TakenSummary.Contains("[Manual Diagnostic Session]") == true)
                {
                    snapshot.TakenReason = "Diagnostic Session";
                }
                else if (snapshot.TakenSummary.Contains("[Error]") == true)
                {
                    snapshot.TakenReason = "Error";
                }
                else if (snapshot.TakenSummary.Contains("Request was slower than the Standard Deviation threshold") == true)
                {
                    snapshot.TakenReason = "Slower than StDev";
                }
                else if (snapshot.TakenSummary.Contains("of requests were slow in the last minute starting") == true)
                {
                    snapshot.TakenReason = "Slow Rate in Minute";
                }
                else if (snapshot.TakenSummary.Contains("of requests had errors in the last minute starting") == true)
                {
                    snapshot.TakenReason = "Error Rate in Minute";
                }
                else
                {
                    snapshot.TakenReason = "";
                }

                if ((bool)snapshotToken["fullCallgraph"] == true)
                {
                    snapshot.CallGraphType = "FULL";
                }
                else if ((bool)snapshotToken["delayedCallGraph"] == true)
                {
                    snapshot.CallGraphType = "PARTIAL";
                }
                else
                {
                    snapshot.CallGraphType = "NONE";
                }

                snapshot.HasErrors = (bool)snapshotToken["errorOccurred"];
                snapshot.IsArchived = (bool)snapshotToken["archived"];

                #region Fill in the deeplinks for the snapshot

                // Decide what kind of timerange
                long fromTimeUnix = convertToUnixTimestamp(jobTimeRange.From);
                long toTimeUnix = convertToUnixTimestamp(jobTimeRange.To);
                long differenceInMinutes = (toTimeUnix - fromTimeUnix) / (60000);
                string DEEPLINK_THIS_TIMERANGE = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnix, fromTimeUnix, differenceInMinutes);

                snapshot.ControllerLink = String.Format(DEEPLINK_CONTROLLER, snapshot.Controller, DEEPLINK_THIS_TIMERANGE);
                snapshot.ApplicationLink = String.Format(DEEPLINK_APPLICATION, snapshot.Controller, snapshot.ApplicationID, DEEPLINK_THIS_TIMERANGE);
                snapshot.TierLink = String.Format(DEEPLINK_TIER, snapshot.Controller, snapshot.ApplicationID, snapshot.TierID, DEEPLINK_THIS_TIMERANGE);
                snapshot.NodeLink = String.Format(DEEPLINK_NODE, snapshot.Controller, snapshot.ApplicationID, snapshot.NodeID, DEEPLINK_THIS_TIMERANGE);
                snapshot.BTLink = String.Format(DEEPLINK_BUSINESS_TRANSACTION, snapshot.Controller, snapshot.ApplicationID, snapshot.BTID, DEEPLINK_THIS_TIMERANGE);

                // The snapshot link requires to have the time range is -30 < occuredtime < +30 minutes
                long fromTimeUnixSnapshot = convertToUnixTimestamp(snapshot.OccuredUtc.AddMinutes(-30));
                long toTimeUnixSnapshot = convertToUnixTimestamp(snapshot.OccuredUtc.AddMinutes(+30));
                long differenceInMinutesSnapshot = (toTimeUnixSnapshot - fromTimeUnixSnapshot) / (60000);
                string DEEPLINK_THIS_TIMERANGE_SNAPSHOT = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnixSnapshot, fromTimeUnixSnapshot, differenceInMinutesSnapshot);
                snapshot.SnapshotLink = String.Format(DEEPLINK_SNAPSHOT_OVERVIEW, snapshot.Controller, snapshot.ApplicationID, snapshot.RequestID, DEEPLINK_THIS_TIMERANGE_SNAPSHOT);
                snapshot.DetailLink = "TODO";

                // This is the report link
                string reportFileName = String.Format(
                    REPORT_SNAPSHOT_DETAILS_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(snapshot.Controller).Host),
                    getShortenedEntityNameForFileSystem(snapshot.ApplicationName, snapshot.ApplicationID),
                    getShortenedEntityNameForFileSystem(snapshot.BTName, snapshot.BTID),
                    userExperienceFolderNameMapping[snapshot.UserExperience],
                    snapshot.Occured,
                    snapshot.RequestID);
                string reportFilePath = Path.Combine(
                    reportsFolderPath,
                    SNAPSHOTS_FOLDER_NAME,
                    reportFileName);

                reportFilePath = reportFilePath.Substring(programOptions.OutputJobFolderPath.Length + 1);
                snapshot.DetailLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", reportFilePath);

                #endregion

                #endregion

                #region Process segments

                List<Segment> segmentsList = null;
                List<ExitCall> exitCallsList = null;
                List<ServiceEndpointCall> serviceEndpointCallsList = null;
                List<DetectedError> detectedErrorsList = null;
                List<BusinessData> businessDataList = null;

                JArray snapshotSegmentsList = FileIOHelper.loadJArrayFromFile(snapshotSegmentsDataFilePath);
                if (snapshotSegmentsList != null)
                {
                    // Prepare elements for storage
                    segmentsList = new List<Segment>(snapshotSegmentsList.Count);
                    // Eyeball each segment to have 3 exits on average
                    exitCallsList = new List<ExitCall>(snapshotSegmentsList.Count * 3);
                    serviceEndpointCallsList = new List<ServiceEndpointCall>(snapshotSegmentsList.Count);
                    // Don't know how long this is going to be, so let's just do it
                    detectedErrorsList = new List<DetectedError>();
                    // Don't know how long this one is going to be either
                    businessDataList = new List<BusinessData>();

                    SortedDictionary<string, int> callChainsSnapshot = new SortedDictionary<string, int>();
                    SortedDictionary<string, int> exitTypesSnapshot = new SortedDictionary<string, int>();

                    foreach (JToken snapshotSegmentToken in snapshotSegmentsList)
                    {
                        string snapshotSegmentDataFilePath = Path.Combine(snapshotFolderPath, String.Format(EXTRACT_SNAPSHOT_SEGMENT_DATA_FILE_NAME, snapshotSegmentToken["id"]));
                        string snapshotSegmentErrorFilePath = Path.Combine(snapshotFolderPath, String.Format(EXTRACT_SNAPSHOT_SEGMENT_ERROR_FILE_NAME, snapshotSegmentToken["id"]));

                        JObject snapshotSegmentDetail = FileIOHelper.loadJObjectFromFile(snapshotSegmentDataFilePath);
                        if (snapshotSegmentDetail != null)
                        {
                            #region Fill in Segment data

                            Segment segment = new Segment();
                            segment.Controller = snapshot.Controller;
                            segment.ApplicationName = snapshot.ApplicationName;
                            segment.ApplicationID = snapshot.ApplicationID;
                            segment.TierID = (long)snapshotSegmentToken["applicationComponentId"];
                            segment.TierName = snapshotSegmentToken["applicationComponentName"].ToString();
                            segment.BTID = snapshot.BTID;
                            segment.BTName = snapshot.BTName;
                            segment.NodeID = (long)snapshotSegmentToken["applicationComponentNodeId"];
                            segment.NodeName = snapshotSegmentToken["applicationComponentNodeName"].ToString();

                            segment.OccuredUtc = convertFromUnixTimestamp((long)snapshotSegmentDetail["serverStartTime"]);
                            segment.Occured = segment.OccuredUtc.ToLocalTime();

                            segment.RequestID = snapshotSegmentDetail["requestGUID"].ToString();
                            segment.SegmentID = (long)snapshotSegmentDetail["id"];
                            segment.UserExperience = snapshotSegmentDetail["userExperience"].ToString();
                            segment.Duration = (long)snapshotSegmentDetail["timeTakenInMilliSecs"];
                            // The value here is not in milliseconds, contrary to the name
                            segment.CPUDuration = Math.Round((double)snapshotSegmentDetail["cpuTimeTakenInMilliSecs"] / 1000000, 2);
                            segment.E2ELatency = (long)snapshotSegmentDetail["endToEndLatency"];
                            if (segment.E2ELatency == -1) { segment.E2ELatency = 0; }
                            segment.WaitDuration = (long)snapshotSegmentDetail["totalWaitTime"];
                            segment.BlockDuration = (long)snapshotSegmentDetail["totalBlockTime"];
                            segment.DiagSessionID = snapshotSegmentDetail["diagnosticSessionGUID"].ToString();
                            if (snapshotSegmentDetail["url"] != null) segment.URL = snapshotSegmentDetail["url"].ToString();
                            if (snapshotSegmentDetail["securityID"] != null) { segment.UserPrincipal = snapshotSegmentDetail["securityID"].ToString(); }
                            if (snapshotSegmentDetail["httpSessionID"] != null) { segment.HTTPSessionID = snapshotSegmentDetail["httpSessionID"].ToString(); }

                            segment.TakenSummary = snapshotSegmentDetail["summary"].ToString();
                            if (segment.TakenSummary.Contains("Scheduled Snapshots:") == true)
                            {
                                segment.TakenReason = "Scheduled";
                            }
                            else if (segment.TakenSummary.Contains("[Manual Diagnostic Session]") == true)
                            {
                                segment.TakenReason = "Diagnostic Session";
                            }
                            else if (segment.TakenSummary.Contains("[Error]") == true)
                            {
                                segment.TakenReason = "Error";
                            }
                            else if (segment.TakenSummary.Contains("Request was slower than the Standard Deviation threshold") == true)
                            {
                                segment.TakenReason = "Slower than StDev";
                            }
                            else if (segment.TakenSummary.Contains("of requests were slow in the last minute starting") == true)
                            {
                                segment.TakenReason = "Slow Rate in Minute";
                            }
                            else if (segment.TakenSummary.Contains("of requests had errors in the last minute starting") == true)
                            {
                                segment.TakenReason = "Error Rate in Minute";
                            }
                            else if (segment.TakenSummary.Contains("[Continuing]") == true)
                            {
                                segment.TakenReason = "Continuing";
                            }
                            else
                            {
                                segment.TakenReason = "";
                            }
                            segment.TakenPolicy = snapshotSegmentDetail["deepDivePolicy"].ToString();

                            segment.ThreadID = snapshotSegmentDetail["threadID"].ToString();
                            segment.ThreadName = snapshotSegmentDetail["threadName"].ToString();

                            segment.WarningThreshold = snapshotSegmentDetail["warningThreshold"].ToString();
                            segment.CriticalThreshold = snapshotSegmentDetail["criticalThreshold"].ToString();

                            if ((bool)snapshotSegmentToken["fullCallgraph"] == true)
                            {
                                segment.CallGraphType = "FULL";
                            }
                            else if ((bool)snapshotSegmentToken["delayedCallGraph"] == true)
                            {
                                segment.CallGraphType = "PARTIAL";
                            }
                            else
                            {
                                segment.CallGraphType = "NONE";
                            }

                            segment.HasErrors = (bool)snapshotSegmentDetail["errorOccured"];
                            segment.IsArchived = (bool)snapshotSegmentDetail["archived"];
                            segment.IsAsync = (bool)snapshotSegmentDetail["async"];
                            segment.IsFirstInChain = (bool)snapshotSegmentDetail["firstInChain"];

                            // What is the relationship to the root segment
                            segment.ParentSegmentID = 0;
                            if (segment.IsFirstInChain == false)
                            {
                                if (snapshotSegmentDetail["snapshotExitSequence"] != null)
                                {
                                    // Parent snapshot has snapshotSequenceCounter in exitCalls array
                                    // Child snapshot has snapshotExitSequence value that binds the child snapshot to the parent
                                    //JToken parentSegment = snapshotSegmentsList.Where(s => s["exitCalls"]["snapshotSequenceCounter"].ToString() == snapshotSegmentDetail["snapshotExitSequence"].ToString()).FirstOrDefault();
                                    List<JToken> possibleParentSegments = snapshotSegmentsList.Where(s => s["exitCalls"].Count() > 0).ToList();
                                    foreach (JToken possibleParentSegment in possibleParentSegments)
                                    {
                                        List<JToken> possibleExits = possibleParentSegment["exitCalls"].Where(e => e["snapshotSequenceCounter"].ToString() == snapshotSegmentDetail["snapshotExitSequence"].ToString()).ToList();
                                        if (possibleExits.Count > 0)
                                        {
                                            segment.ParentSegmentID = (long)possibleParentSegment["id"];
                                            break;
                                        }
                                    }
                                }
                                if (segment.ParentSegmentID == 0)
                                {
                                    // Some async snapshots can have no initiating parent
                                    // Do nothing
                                    // OR!
                                    // This can happen when the parent snapshot got an exception calling downstream tier, both producing snapshot, but parent snapshot doesn't have a call graph
                                    // But sometimes non-async ones have funny parenting
                                }
                            }
                            segment.ParentTierName = snapshotSegmentToken["callingComponent"].ToString();

                            #endregion

                            #region Fill in the deeplinks for the segment

                            segment.ControllerLink = snapshot.ControllerLink;
                            segment.ApplicationLink = snapshot.ApplicationLink;
                            segment.TierLink = String.Format(DEEPLINK_TIER, segment.Controller, segment.ApplicationID, segment.TierID, DEEPLINK_THIS_TIMERANGE);
                            segment.NodeLink = String.Format(DEEPLINK_NODE, segment.Controller, segment.ApplicationID, segment.NodeID, DEEPLINK_THIS_TIMERANGE);
                            segment.BTLink = snapshot.BTLink;

                            // The snapshot link requires to have the time range is -30 < occuredtime < +30 minutes
                            fromTimeUnixSnapshot = convertToUnixTimestamp(snapshot.OccuredUtc.AddMinutes(-30));
                            toTimeUnixSnapshot = convertToUnixTimestamp(snapshot.OccuredUtc.AddMinutes(+30));
                            differenceInMinutesSnapshot = (toTimeUnixSnapshot - fromTimeUnixSnapshot) / (60000);
                            DEEPLINK_THIS_TIMERANGE_SNAPSHOT = String.Format(DEEPLINK_TIMERANGE_BETWEEN_TIMES, toTimeUnixSnapshot, fromTimeUnixSnapshot, differenceInMinutesSnapshot);
                            segment.SegmentLink = String.Format(DEEPLINK_SNAPSHOT_SEGMENT, segment.Controller, segment.ApplicationID, segment.RequestID, segment.SegmentID, DEEPLINK_THIS_TIMERANGE_SNAPSHOT);

                            #endregion

                            #region Get segment's call chain and make it pretty

                            // Convert call chain to something readable
                            // This is raw:
                            //Component:108|Exit Call:JMS|To:{[UNRESOLVED][115]}|Component:{[UNRESOLVED][115]}|Exit Call:JMS|To:115|Component:115
                            //^^^^^^^^^^^^^ ECommerce-Services
                            //              ^^^^^^^^^^^^^ JMS
                            //							^^^^^^^^^^^^^^^^^^^^^^ Active MQ-OrderQueue
                            //												   ^^^^^^^^^^^^^^^^^^^^^^^^^^^^ JMS
                            //																				^^^^^^^^^^^^^^ 
                            //																							   ^^^^^^^ Order-Processing-Services
                            //																									   ^^^^^^^^^^^^ Order-Processing-Services
                            // This is what I want it to look like:
                            // ECommerce-Services->[JMS]->Active MQ-OrderQueue->[JMS]->Order-Processing-Services
                            // 
                            // This is raw:
                            //Component:108|Exit Call:WEB_SERVICE|To:111|Component:111
                            //^^^^^^^^^^^^^ ECommerce-Services
                            //              ^^^^^^^^^^^^^^^^^^^^^ WEB_SERVICE
                            //                                    ^^^^^^ Inventory-Services
                            //                                           ^^^^^^ Inventory-Services
                            // This is what I want it to look like:
                            // ECommerce-Services->[WEB_SERVICE]->Inventory-Services
                            string callChainForThisSegment = snapshotSegmentDetail["callChain"].ToString();
                            string[] callChainTokens = callChainForThisSegment.Split('|');
                            StringBuilder sbCallChain = new StringBuilder();
                            foreach (string callChainToken in callChainTokens)
                            {
                                if (callChainToken.StartsWith("Component") == true)
                                {
                                    long tierID = -1;
                                    if (long.TryParse(callChainToken.Substring(10), out tierID) == true)
                                    {
                                        if (tiersList != null)
                                        {
                                            EntityTier tier = tiersList.Where(t => t.TierID == tierID).FirstOrDefault();
                                            if (tier != null)
                                            {
                                                sbCallChain.AppendFormat("({0})->", tier.TierName);
                                            }
                                        }
                                    }
                                }
                                else if (callChainToken.StartsWith("Exit Call") == true)
                                {
                                    sbCallChain.AppendFormat("[{0}]->", callChainToken.Substring(10));
                                }
                                else if (callChainToken.StartsWith("To:{[UNRESOLVED]") == true)
                                {
                                    long backendID = -1;
                                    if (long.TryParse(callChainToken.Substring(17).TrimEnd(']', '}'), out backendID) == true)
                                    {
                                        if (backendsList != null)
                                        {
                                            EntityBackend backend = backendsList.Where(b => b.BackendID == backendID).FirstOrDefault();
                                            if (backend != null)
                                            {
                                                //sbCallChain.AppendFormat("<{0}><{1}>>->", backendRow.BackendName, backendRow.BackendType);
                                                sbCallChain.AppendFormat("<{0}>->", backend.BackendName);
                                            }
                                        }
                                    }
                                }
                            }
                            if (sbCallChain.Length > 2)
                            {
                                sbCallChain.Remove(sbCallChain.Length - 2, 2);
                            }
                            callChainForThisSegment = sbCallChain.ToString();

                            #endregion

                            #region Process Exits in Segment

                            List<ExitCall> exitCallsListInThisSegment = new List<ExitCall>();
                            foreach (JToken exitCallToken in snapshotSegmentDetail["snapshotExitCalls"])
                            {
                                #region Parse the exit call into correct exit

                                ExitCall exitCall = new ExitCall();

                                exitCall.Controller = segment.Controller;
                                exitCall.ApplicationName = segment.ApplicationName;
                                exitCall.ApplicationID = segment.ApplicationID;
                                exitCall.TierID = segment.TierID;
                                exitCall.TierName = segment.TierName;
                                exitCall.BTID = segment.BTID;
                                exitCall.BTName = segment.BTName;
                                exitCall.NodeID = segment.NodeID;
                                exitCall.NodeName = segment.NodeName;

                                exitCall.RequestID = segment.RequestID;
                                exitCall.SegmentID = segment.SegmentID;

                                exitCall.ExitType = exitCallToken["exitPointName"].ToString();

                                // Create pretty call chain
                                // Where are we going, Tier or Backend
                                if (exitCallToken["toComponentId"].ToString().StartsWith("{[UNRESOLVED]") == true)
                                {
                                    // Backend
                                    exitCall.ToEntityType = "Backend";
                                    JToken goingToProperty = exitCallToken["properties"].Where(p => p["name"].ToString() == "backendId").FirstOrDefault();
                                    if (goingToProperty != null)
                                    {
                                        exitCall.ToEntityID = (long)goingToProperty["value"]; ;
                                    }
                                    goingToProperty = exitCallToken["properties"].Where(p => p["name"].ToString() == "to").FirstOrDefault();
                                    if (goingToProperty != null)
                                    {
                                        exitCall.ToEntityName = goingToProperty["value"].ToString();
                                    }
                                    exitCall.CallChain = String.Format("{0}->[{1}]-><{2}>", callChainForThisSegment, exitCall.ExitType, exitCall.ToEntityName);
                                }
                                else if (exitCallToken["toComponentId"].ToString().StartsWith("App:") == true)
                                {
                                    //Application
                                    exitCall.ToEntityType = "Application";
                                    JToken goingToProperty = exitCallToken["properties"].Where(p => p["name"].ToString() == "appId").FirstOrDefault();
                                    if (goingToProperty != null)
                                    {
                                        exitCall.ToEntityID = (long)goingToProperty["value"]; ;
                                    }
                                    goingToProperty = exitCallToken["properties"].Where(p => p["name"].ToString() == "to").FirstOrDefault();
                                    if (goingToProperty != null)
                                    {
                                        exitCall.ToEntityName = goingToProperty["value"].ToString();
                                    }
                                    exitCall.CallChain = String.Format("{0}->[{1}]->(({2}))", callChainForThisSegment, exitCall.ExitType, exitCall.ToEntityName);
                                }
                                else
                                {
                                    // Tier
                                    exitCall.ToEntityType = "Tier";
                                    exitCall.ToEntityID = (long)exitCallToken["toComponentId"];
                                    JToken goingToProperty = exitCallToken["properties"].Where(p => p["name"].ToString() == "to").FirstOrDefault();
                                    if (goingToProperty != null)
                                    {
                                        exitCall.ToEntityName = goingToProperty["value"].ToString();
                                    }
                                    exitCall.CallChain = String.Format("{0}->[{1}]->({2})", callChainForThisSegment, exitCall.ExitType, exitCall.ToEntityName);
                                }

                                exitCall.Detail = exitCallToken["detailString"].ToString();
                                exitCall.ErrorDetail = exitCallToken["errorDetails"].ToString();
                                if (exitCall.ErrorDetail == "\\N") { exitCall.ErrorDetail = String.Empty; }
                                exitCall.Method = exitCallToken["callingMethod"].ToString();

                                exitCall.Duration = (long)exitCallToken["timeTakenInMillis"];
                                exitCall.IsAsync = ((bool)exitCallToken["exitPointCall"]["synchronous"] == false);

                                // Parse Properties
                                exitCall.PropsAll = exitCallToken["propertiesAsString"].ToString();
                                int i = 0;
                                foreach (JToken customExitPropertyToken in exitCallToken["properties"])
                                {
                                    exitCall.NumProps++;
                                    string propertyName = customExitPropertyToken["name"].ToString();
                                    string propertyValue = customExitPropertyToken["value"].ToString();
                                    switch (propertyName)
                                    {
                                        case "component":
                                        case "to":
                                        case "from":
                                        case "backendId":
                                            // Ignore those, already mapped elsewhere
                                            exitCall.NumProps--;
                                            break;
                                        case "Query Type":
                                            exitCall.PropQueryType = propertyValue;
                                            break;
                                        case "Statement Type":
                                            exitCall.PropStatementType = propertyValue;
                                            break;
                                        case "URL":
                                            exitCall.PropURL = propertyValue;
                                            break;
                                        case "Service":
                                            exitCall.PropServiceName = propertyValue;
                                            break;
                                        case "Operation":
                                            exitCall.PropOperationName = propertyValue;
                                            break;
                                        case "Name":
                                            exitCall.PropName = propertyValue;
                                            break;
                                        case "Asynchronous":
                                            exitCall.PropAsync = propertyValue;
                                            break;
                                        case "Continuation":
                                            exitCall.PropContinuation = propertyValue;
                                            break;
                                        default:
                                            i++;
                                            // Have 5 overflow buckets for those, hope it is enough
                                            if (i == 1)
                                            {
                                                exitCall.Prop1Name = propertyName;
                                                exitCall.Prop1Value = propertyValue;
                                            }
                                            else if (i == 2)
                                            {
                                                exitCall.Prop2Name = propertyName;
                                                exitCall.Prop2Value = propertyValue;
                                            }
                                            else if (i == 3)
                                            {
                                                exitCall.Prop3Name = propertyName;
                                                exitCall.Prop3Value = propertyValue;
                                            }
                                            else if (i == 4)
                                            {
                                                exitCall.Prop4Name = propertyName;
                                                exitCall.Prop4Value = propertyValue;
                                            }
                                            else if (i == 5)
                                            {
                                                exitCall.Prop5Name = propertyName;
                                                exitCall.Prop5Value = propertyValue;
                                            }
                                            break;
                                    }
                                }

                                exitCall.NumCalls = (int)exitCallToken["count"];
                                exitCall.NumErrors = (int)exitCallToken["errorCount"];
                                exitCall.HasErrors = exitCall.NumErrors != 0;

                                #endregion

                                #region Fill in the deeplinks for the exit

                                exitCall.ControllerLink = segment.Controller;
                                exitCall.ApplicationLink = segment.ApplicationLink;
                                exitCall.TierLink = segment.TierLink;
                                exitCall.NodeLink = segment.NodeLink;
                                exitCall.BTLink = segment.BTLink;

                                switch (exitCall.ToEntityType)
                                {
                                    case "Backend":
                                        exitCall.ToLink = String.Format(DEEPLINK_BACKEND, exitCall.Controller, exitCall.ApplicationID, exitCall.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                                        break;

                                    case "Tier":
                                        exitCall.ToLink = String.Format(DEEPLINK_TIER, exitCall.Controller, exitCall.ApplicationID, exitCall.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                                        break;

                                    case "Application":
                                        exitCall.ToLink = String.Format(DEEPLINK_APPLICATION, exitCall.Controller, exitCall.ToEntityID, DEEPLINK_THIS_TIMERANGE);
                                        break;

                                    default:
                                        break;
                                }

                                #endregion

                                exitCallsListInThisSegment.Add(exitCall);
                            }

                            #endregion

                            #region Process Service Endpoints in Segment

                            List<ServiceEndpointCall> serviceEndpointCallsListInThisSegment = new List<ServiceEndpointCall>();
                            foreach (JToken serviceEndpointToken in snapshotSegmentDetail["serviceEndPointIds"])
                            {
                                long serviceEndpointID = (long)((JValue)serviceEndpointToken).Value;
                                if (serviceEndpointsList != null)
                                {
                                    EntityServiceEndpoint serviceEndpoint = serviceEndpointsList.Where(s => s.SEPID == serviceEndpointID).FirstOrDefault();
                                    if (serviceEndpoint != null)
                                    {
                                        #region Fill Service Endpoint stuff

                                        ServiceEndpointCall serviceEndpointCall = new ServiceEndpointCall();

                                        serviceEndpointCall.Controller = segment.Controller;
                                        serviceEndpointCall.ApplicationName = segment.ApplicationName;
                                        serviceEndpointCall.ApplicationID = segment.ApplicationID;
                                        serviceEndpointCall.TierID = segment.TierID;
                                        serviceEndpointCall.TierName = segment.TierName;
                                        serviceEndpointCall.BTID = segment.BTID;
                                        serviceEndpointCall.BTName = segment.BTName;
                                        serviceEndpointCall.NodeID = segment.NodeID;
                                        serviceEndpointCall.NodeName = segment.NodeName;

                                        serviceEndpointCall.RequestID = segment.RequestID;
                                        serviceEndpointCall.SegmentID = segment.SegmentID;

                                        serviceEndpointCall.SEPID = serviceEndpoint.SEPID;
                                        serviceEndpointCall.SEPName = serviceEndpoint.SEPName;
                                        serviceEndpointCall.SEPType = serviceEndpoint.SEPType;

                                        #endregion

                                        #region Fill in the deeplinks for the Service Endpoint

                                        serviceEndpointCall.ControllerLink = segment.Controller;
                                        serviceEndpointCall.ApplicationLink = segment.ApplicationLink;
                                        serviceEndpointCall.TierLink = segment.TierLink;
                                        serviceEndpointCall.NodeLink = segment.NodeLink;
                                        serviceEndpointCall.BTLink = segment.BTLink;
                                        serviceEndpointCall.SEPLink = String.Format(DEEPLINK_SERVICE_ENDPOINT, serviceEndpointCall.Controller, serviceEndpointCall.ApplicationID, serviceEndpointCall.TierID, serviceEndpointCall.SEPID, DEEPLINK_THIS_TIMERANGE);

                                        #endregion

                                        serviceEndpointCallsListInThisSegment.Add(serviceEndpointCall);
                                    }
                                }
                            }

                            #endregion

                            #region Process Errors in Segment

                            segment.NumErrors = snapshotSegmentDetail["errorIDs"].Count();
                            List<DetectedError> detectedErrorsListInThisSegment = new List<DetectedError>();
                            if (segment.NumErrors > 0)
                            {
                                // First, populate the list of errors from the reported error numbers
                                List<DetectedError> detectedErrorsFromErrorIDs = new List<DetectedError>(segment.NumErrors);
                                foreach (JToken errorToken in snapshotSegmentDetail["errorIDs"])
                                {
                                    long errorID = (long)((JValue)errorToken).Value;
                                    if (errorsList != null)
                                    {
                                        EntityError error = errorsList.Where(e => e.ErrorID == errorID).FirstOrDefault();
                                        if (error != null)
                                        {
                                            DetectedError detectedError = new DetectedError();

                                            detectedError.Controller = segment.Controller;
                                            detectedError.ApplicationName = segment.ApplicationName;
                                            detectedError.ApplicationID = segment.ApplicationID;
                                            detectedError.TierID = segment.TierID;
                                            detectedError.TierName = segment.TierName;
                                            detectedError.BTID = segment.BTID;
                                            detectedError.BTName = segment.BTName;
                                            detectedError.NodeID = segment.NodeID;
                                            detectedError.NodeName = segment.NodeName;

                                            detectedError.RequestID = segment.RequestID;
                                            detectedError.SegmentID = segment.SegmentID;

                                            detectedError.ErrorID = error.ErrorID;
                                            detectedError.ErrorName = error.ErrorName;
                                            detectedError.ErrorType = error.ErrorType;

                                            detectedError.ErrorIDMatchedToMessage = false;

                                            detectedError.ErrorMessage = "<unmatched>";
                                            detectedError.ErrorDetail = "<unmatched>";

                                            detectedErrorsFromErrorIDs.Add(detectedError);
                                        }
                                    }
                                }

                                // Second, populate the list of the details of errors
                                JArray snapshotSegmentErrorDetail = FileIOHelper.loadJArrayFromFile(snapshotSegmentErrorFilePath);
                                if (snapshotSegmentErrorDetail != null)
                                {
                                    detectedErrorsListInThisSegment = new List<DetectedError>(snapshotSegmentErrorDetail.Count);

                                    foreach (JToken errorToken in snapshotSegmentErrorDetail)
                                    {
                                        DetectedError detectedError = new DetectedError();

                                        detectedError.Controller = segment.Controller;
                                        detectedError.ApplicationName = segment.ApplicationName;
                                        detectedError.ApplicationID = segment.ApplicationID;
                                        detectedError.TierID = segment.TierID;
                                        detectedError.TierName = segment.TierName;
                                        detectedError.BTID = segment.BTID;
                                        detectedError.BTName = segment.BTName;
                                        detectedError.NodeID = segment.NodeID;
                                        detectedError.NodeName = segment.NodeName;

                                        detectedError.RequestID = segment.RequestID;
                                        detectedError.SegmentID = segment.SegmentID;

                                        detectedError.ErrorID = -1;
                                        detectedError.ErrorName = "?";
                                        detectedError.ErrorType = "?";

                                        detectedError.ErrorMessage = errorToken["name"].ToString();
                                        detectedError.ErrorDetail = errorToken["value"].ToString().Replace("AD_STACK_TRACE:", "\n").Replace("__AD_CMSG__", "\n");

                                        detectedErrorsListInThisSegment.Add(detectedError);
                                    }
                                }

                                // Now reconcile them both
                                #region Explanation of all of this nonsense to parse the errors

                                // The IDs of the errors give us what errors occured
                                // But the segment JSON does not include all the errors
                                // The JSON in segment error detailsdoesn't include error number
                                // However, we get multipe error instances for each of the errors
                                // Here we have to do some serious gymnastics to match the detected error with what is in JSON
                                // 
                                // Segment may say:
                                //"errorIDs" : [ 4532 ],
                                //"errorDetails" : [ {
                                //  "id" : 0,
                                //  "version" : 0,
                                //  "name" : "Internal Server Error : 500",
                                //  "value" : "HTTP error code : 500"
                                //}
                                // Error detail says:
                                //{
                                //  "id" : 286452959,
                                //  "version" : 0,
                                //  "name" : "Internal Server Error : 500",
                                //  "value" : "HTTP error code : 500"
                                //}
                                // -------------------------------
                                // Sometimes segment has no details:
                                //"errorIDs" : [ 66976 ],
                                //"errorDetails" : [ ],
                                // Where:
                                // 66976        TRBException : COMException
                                // But the details are there:
                                //[ {
                                //  "id" : 171771942,
                                //  "version" : 0,
                                //  "name" : "Corillian.Voyager.ExecutionServices.Client.TRBException:Corillian.Voyager.ExecutionServices.Client.TRBException",
                                //  "value" : "Unknown Voyager Connectivity Error: C0000FA5__AD_CMSG__System.Runtime.InteropServices.COMException (0xC0000FA5): Execute: Session doesn't exist or has timed out in TP TP41-SVAKSA69901MXK\r\n   at Corillian.Platform.Router.VoyagerLoadBalancer.Execute(String sKey, String sRequest, String& sResponse)\r\n   at Corillian.Voyager.VoyagerInterface.Client.VlbConnector.Execute(String voyagerCommandString, String sessionId, String userId, String FI)AD_STACK_TRACE:Corillian.Voyager.ExecutionServices.Client.TRBException: at Corillian.Voyager.VoyagerInterface.Client.VlbConnector.Void HandleCOMException(System.Runtime.InteropServices.COMException)() at Corillian.Voyager.VoyagerInterface.Client.VlbConnector.System.String Execute(System.String, System.String, System.String, System.String)() at Corillian.Voyager.ExecutionServices.Client.VoyagerService.System.String Execute(Corillian.Voyager.Common.IRequest, System.String, System.String, System.String)() at Corillian.Voyager.ExecutionServices.Client.VoyagerService.System.String Execute(Corillian.Voyager.Common.IRequest)() at USB.Banking.Operations.BankingServiceProxy.USB.Banking.Messages.USBGetAccountsResponse GetAccounts(USB.Banking.Messages.USBGetAccountsRequest)() at Corillian.AppsUI.Web.Models.Accounts.AccountServiceProxy.USB.Banking.Messages.USBGetAccountsResponse Corillian.AppsUI.Web.Models.Accounts.IAccountServiceProxy.GetAllAccounts(Boolean, Boolean, Boolean, Boolean)() at Corillian.AppsUI.Web.Models.Accounts.AccountServiceProxy.USB.Banking.Messages.USBGetAccountsResponse Corillian.AppsUI.Web.Models.Accounts.IAccountServiceProxy.GetAllAccounts(Boolean)() at Castle.Proxies.Invocations.IAccountServiceProxy_GetAllAccounts.Void InvokeMethodOnTarget()() at Castle.DynamicProxy.AbstractInvocation.Void Proceed()() at USB.DigitalChannel.DigitalUI.Helpers.Logging.LoggingInterceptor.Void Intercept(Castle.DynamicProxy.IInvocation)() at Castle.DynamicProxy.AbstractInvocation.Void Proceed()() at Castle.Proxies.IAccountServiceProxyProxy.USB.Banking.Messages.USBGetAccountsResponse GetAllAccounts(Boolean)() at Corillian.AppsUI.Web.Models.PaymentCentral.PaymentCentralService.Corillian.AppsUI.Web.Models.PaymentCentral.AccountBalancesResponseContainer GetAccountBalances(Corillian.AppsUI.Web.Models.PaymentCentral.AccountBalancesRequest)() at Corillian.AppsUI.Web.Models.PaymentCentral.PaymentCentralService.Corillian.AppsUI.Web.Models.PaymentCentral.UserAndAccountsResponse GetUserAndAccounts(Corillian.AppsUI.Web.Models.PaymentCentral.AccountBalancesRequest)() at Castle.Proxies.Invocations.IPaymentCentralService_GetUserAndAccounts.Void InvokeMethodOnTarget()() at Castle.DynamicProxy.AbstractInvocation.Void Proceed()() at USB.DigitalChannel.DigitalUI.Helpers.Logging.LoggingInterceptor.Void Intercept(Castle.DynamicProxy.IInvocation)() at Castle.DynamicProxy.AbstractInvocation.Void Proceed()() at Castle.Proxies.IPaymentCentralServiceProxy.Corillian.AppsUI.Web.Models.PaymentCentral.UserAndAccountsResponse GetUserAndAccounts(Corillian.AppsUI.Web.Models.PaymentCentral.AccountBalancesRequest)() at Corillian.AppsUI.Web.AsyncGetUserAndAccounts.System.String GetUserAndAccounts()() at Corillian.AppsUI.Web.AsyncGetUserAndAccounts.System.String get_TaskResult()() at USB.DigitalChannel.CommonUI.Controllers.BaseController.Void GetAsyncData(USB.DigitalChannel.CommonUI.Models.shared.BaseModel)() at Corillian.AppsUI.Web.Controllers.BaseDashboardController.Void GetWebAsyncData(Corillian.AppsUI.Web.Models.Shared.DashboardBaseModel)() at Corillian.AppsUI.Web.Controllers.CustomerDashboardController.System.Web.Mvc.ActionResult Index()() at .System.Object lambda_method(System.Runtime.CompilerServices.ExecutionScope, System.Web.Mvc.ControllerBase, System.Object[])() at System.Web.Mvc.ReflectedActionDescriptor.System.Object Execute(System.Web.Mvc.ControllerContext, System.Collections.Generic.IDictionary`2[System.String,System.Object])() at System.Web.Mvc.ControllerActionInvoker.System.Web.Mvc.ActionResult InvokeActionMethod(System.Web.Mvc.ControllerContext, System.Web.Mvc.ActionDescriptor, System.Collections.Generic.IDictionary`2[System.String,System.Object])() at System.Web.Mvc.ControllerActionInvoker+<>c__DisplayClassd.System.Web.Mvc.ActionExecutedContext <InvokeActionMethodWithFilters>b__a()() at System.Web.Mvc.ControllerActionInvoker.System.Web.Mvc.ActionExecutedContext InvokeActionMethodFilter(System.Web.Mvc.IActionFilter, System.Web.Mvc.ActionExecutingContext, System.Func`1[System.Web.Mvc.ActionExecutedContext])() at System.Web.Mvc.ControllerActionInvoker.System.Web.Mvc.ActionExecutedContext InvokeActionMethodFilter(System.Web.Mvc.IActionFilter, System.Web.Mvc.ActionExecutingContext, System.Func`1[System.Web.Mvc.ActionExecutedContext])() at System.Web.Mvc.ControllerActionInvoker.System.Web.Mvc.ActionExecutedContext InvokeActionMethodFilter(System.Web.Mvc.IActionFilter, System.Web.Mvc.ActionExecutingContext, System.Func`1[System.Web.Mvc.ActionExecutedContext])() at System.Web.Mvc.ControllerActionInvoker.System.Web.Mvc.ActionExecutedContext InvokeActionMethodFilter(System.Web.Mvc.IActionFilter, System.Web.Mvc.ActionExecutingContext, System.Func`1[System.Web.Mvc.ActionExecutedContext])() at System.Web.Mvc.ControllerActionInvoker.System.Web.Mvc.ActionExecutedContext InvokeActionMethodWithFilters(System.Web.Mvc.ControllerContext, System.Collections.Generic.IList`1[System.Web.Mvc.IActionFilter], System.Web.Mvc.ActionDescriptor, System.Collections.Generic.IDictionary`2[System.String,System.Object])() at System.Web.Mvc.ControllerActionInvoker.Boolean InvokeAction(System.Web.Mvc.ControllerContext, System.String)() at System.Web.Mvc.Controller.Void ExecuteCore()() at System.Web.Mvc.ControllerBase.Execute() at System.Web.Mvc.MvcHandler+<>c__DisplayClass8.<BeginProcessRequest>b__4() at System.Web.Mvc.Async.AsyncResultWrapper+<>c__DisplayClass1.<MakeVoidDelegate>b__0() at System.Web.Mvc.Async.AsyncResultWrapper+<>c__DisplayClass8`1.<BeginSynchronous>b__7() at System.Web.Mvc.Async.AsyncResultWrapper+WrappedAsyncResult`1.End() at System.Web.Mvc.Async.AsyncResultWrapper.End() at System.Web.Mvc.Async.AsyncResultWrapper.End() at Microsoft.Web.Mvc.MvcDynamicSessionHandler.EndProcessRequest() at System.Web.HttpApplication+CallHandlerExecutionStep.System.Web.HttpApplication.IExecutionStep.Execute() at System.Web.HttpApplication.ExecuteStep() at System.Web.HttpApplication+PipelineStepManager.ResumeSteps() at System.Web.HttpApplication.BeginProcessRequestNotification() at System.Web.HttpRuntime.ProcessRequestNotificationPrivate() at System.Web.Hosting.PipelineRuntime.ProcessRequestNotificationHelper() at System.Web.Hosting.PipelineRuntime.ProcessRequestNotification() at System.Web.Hosting.PipelineRuntime.ProcessRequestNotificationHelper() at System.Web.Hosting.PipelineRuntime.ProcessRequestNotification() Caused by: Corillian.Voyager.ExecutionServices.Client.TRBException  at Corillian.Platform.Router.VoyagerLoadBalancer.Void Execute(System.String, System.String, System.String ByRef)()  ... 17 more "
                                //} ]
                                // -------------------------------
                                // Sometimes segment says:
                                //"errorIDs" : [ 131789, 3002 ],
                                //"errorDetails" : [ {
                                //  "id" : 0,
                                //  "version" : 0,
                                //  "name" : "1. USB.OLBService.Handlers.TransactionUtilities",
                                //  "value" : "USB.OLBService.Handlers.TransactionUtilities : Error occurred in MapHostTransactions: System.NullReferenceException: Object reference not set to an instance of an object.\r\n   at USB.OLBService.Handlers.TransactionUtilities.MapCheckCardHostResponseTransactions(GetOutStandingAuthRequest requestFromUI, List`1 transactions, USBAccount actualAcct)"
                                //} ],
                                // Where:
                                // 131789   MessageQueueException
                                // 3002     .NET Logger Error Messages
                                // But the list of errors looks like that:
                                //[ {
                                //  "id" : 171889775,
                                //  "version" : 0,
                                //  "name" : "System.Messaging.MessageQueueException",
                                //  "value" : "Insufficient resources to perform operation.AD_STACK_TRACE:System.Messaging.MessageQueueException: at System.Messaging.MessageQueue.SendInternal() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Audit.AuditTrxSender.Audit() at USB.DigitalServices.Audit.MessageReceiver.Process() at USB.OLBService.Handlers.Utilities.Audit() at USB.OLBService.Handlers.GetTransactionTypes.Execute() at USB.OLBService.Handlers.TransactionUtilities.MapHostResponseTransactions() at USB.OLBService.Handlers.TransactionUtilities.GetMonitoryListExecutor() at USB.OLBService.Handlers.TransactionUtilities.GetHostHistory() at USB.OLBService.Handlers.GetPagedTransactionsV2.Execute() at USB.DCISService.Accounts.V1.Handlers.GetAccountTransactionsV4.Execute() at Fiserv.AppService.Core.HandlerBase`1.Execute() at Fiserv.AppService.Core.ServiceProcessor.Process() at USB.DCIS.Server.DCISServiceServer.Execute() at .SyncInvokeExecute() at System.ServiceModel.Dispatcher.SyncMethodInvoker.Invoke() at System.ServiceModel.Dispatcher.DispatchOperationRuntime.InvokeBegin() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage5() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage4() at System.ServiceModel.Dispatcher.MessageRpc.Process() at System.ServiceModel.Dispatcher.ChannelHandler.DispatchAndReleasePump() at System.ServiceModel.Dispatcher.ChannelHandler.HandleRequest() at System.ServiceModel.Dispatcher.ChannelHandler.AsyncMessagePump() at System.ServiceModel.Diagnostics.Utility+AsyncThunk.UnhandledExceptionFrame() at System.ServiceModel.AsyncResult.Complete() at System.ServiceModel.Channels.InputQueue`1+AsyncQueueReader.Set() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueueChannel`1.EnqueueAndDispatch() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.HttpChannelListener.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpTransportManager.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.BeginRequest() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequest() at System.ServiceModel.PartialTrustHelpers.PartialTrustInvoke() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequestWithFlow() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke2() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.ProcessCallbacks() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.CompletionCallback() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+ScheduledOverlapped.IOCallback() at System.ServiceModel.Diagnostics.Utility+IOCompletionThunk.UnhandledExceptionFrame() at System.Threading._IOCompletionCallback.PerformIOCompletionCallback() "
                                //}, {
                                //  "id" : 171889775,
                                //  "version" : 0,
                                //  "name" : "System.Messaging.MessageQueueException",
                                //  "value" : "Insufficient resources to perform operation.AD_STACK_TRACE:System.Messaging.MessageQueueException: at System.Messaging.MessageQueue.SendInternal() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Audit.AuditTrxSender.Audit() at USB.DigitalServices.Audit.MessageReceiver.Process() at USB.OLBService.Handlers.Utilities.Audit() at USB.OLBService.Handlers.GetTransactionTypes.Execute() at USB.OLBService.Handlers.TransactionUtilities.MapCheckCardHostResponseTransactions() at USB.OLBService.Handlers.TransactionUtilities.GetCheckCardAuthorizationsFromHost() at USB.OLBService.Handlers.GetPagedTransactionsV2.GetDebitCardAuthorizationTransactions() at USB.OLBService.Handlers.GetPagedTransactionsV2.Execute() at USB.DCISService.Accounts.V1.Handlers.GetAccountTransactionsV4.Execute() at Fiserv.AppService.Core.HandlerBase`1.Execute() at Fiserv.AppService.Core.ServiceProcessor.Process() at USB.DCIS.Server.DCISServiceServer.Execute() at .SyncInvokeExecute() at System.ServiceModel.Dispatcher.SyncMethodInvoker.Invoke() at System.ServiceModel.Dispatcher.DispatchOperationRuntime.InvokeBegin() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage5() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage4() at System.ServiceModel.Dispatcher.MessageRpc.Process() at System.ServiceModel.Dispatcher.ChannelHandler.DispatchAndReleasePump() at System.ServiceModel.Dispatcher.ChannelHandler.HandleRequest() at System.ServiceModel.Dispatcher.ChannelHandler.AsyncMessagePump() at System.ServiceModel.Diagnostics.Utility+AsyncThunk.UnhandledExceptionFrame() at System.ServiceModel.AsyncResult.Complete() at System.ServiceModel.Channels.InputQueue`1+AsyncQueueReader.Set() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueueChannel`1.EnqueueAndDispatch() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.HttpChannelListener.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpTransportManager.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.BeginRequest() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequest() at System.ServiceModel.PartialTrustHelpers.PartialTrustInvoke() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequestWithFlow() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke2() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.ProcessCallbacks() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.CompletionCallback() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+ScheduledOverlapped.IOCallback() at System.ServiceModel.Diagnostics.Utility+IOCompletionThunk.UnhandledExceptionFrame() at System.Threading._IOCompletionCallback.PerformIOCompletionCallback() "
                                //}, {
                                //  "id" : 171889775,
                                //  "version" : 0,
                                //  "name" : "System.Messaging.MessageQueueException",
                                //  "value" : "Insufficient resources to perform operation.AD_STACK_TRACE:System.Messaging.MessageQueueException: at System.Messaging.MessageQueue.SendInternal() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Audit.AuditTrxSender.Audit() at USB.DigitalServices.Audit.MessageReceiver.Process() at USB.OLBService.Handlers.Utilities.Audit() at USB.OLBService.Handlers.GetPagedTransactionsV2.GetDebitCardAuthorizationTransactions() at USB.OLBService.Handlers.GetPagedTransactionsV2.Execute() at USB.DCISService.Accounts.V1.Handlers.GetAccountTransactionsV4.Execute() at Fiserv.AppService.Core.HandlerBase`1.Execute() at Fiserv.AppService.Core.ServiceProcessor.Process() at USB.DCIS.Server.DCISServiceServer.Execute() at .SyncInvokeExecute() at System.ServiceModel.Dispatcher.SyncMethodInvoker.Invoke() at System.ServiceModel.Dispatcher.DispatchOperationRuntime.InvokeBegin() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage5() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage4() at System.ServiceModel.Dispatcher.MessageRpc.Process() at System.ServiceModel.Dispatcher.ChannelHandler.DispatchAndReleasePump() at System.ServiceModel.Dispatcher.ChannelHandler.HandleRequest() at System.ServiceModel.Dispatcher.ChannelHandler.AsyncMessagePump() at System.ServiceModel.Diagnostics.Utility+AsyncThunk.UnhandledExceptionFrame() at System.ServiceModel.AsyncResult.Complete() at System.ServiceModel.Channels.InputQueue`1+AsyncQueueReader.Set() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueueChannel`1.EnqueueAndDispatch() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.HttpChannelListener.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpTransportManager.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.BeginRequest() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequest() at System.ServiceModel.PartialTrustHelpers.PartialTrustInvoke() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequestWithFlow() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke2() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.ProcessCallbacks() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.CompletionCallback() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+ScheduledOverlapped.IOCallback() at System.ServiceModel.Diagnostics.Utility+IOCompletionThunk.UnhandledExceptionFrame() at System.Threading._IOCompletionCallback.PerformIOCompletionCallback() "
                                //}, {
                                //  "id" : 171889775,
                                //  "version" : 0,
                                //  "name" : "System.Messaging.MessageQueueException",
                                //  "value" : "Insufficient resources to perform operation.AD_STACK_TRACE:System.Messaging.MessageQueueException: at System.Messaging.MessageQueue.SendInternal() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Audit.AuditTrxSender.Audit() at USB.DigitalServices.Audit.MessageReceiver.Process() at USB.OLBService.Handlers.Utilities.Audit() at USB.OLBService.Handlers.GetPagedTransactionsV2.Execute() at USB.DCISService.Accounts.V1.Handlers.GetAccountTransactionsV4.Execute() at Fiserv.AppService.Core.HandlerBase`1.Execute() at Fiserv.AppService.Core.ServiceProcessor.Process() at USB.DCIS.Server.DCISServiceServer.Execute() at .SyncInvokeExecute() at System.ServiceModel.Dispatcher.SyncMethodInvoker.Invoke() at System.ServiceModel.Dispatcher.DispatchOperationRuntime.InvokeBegin() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage5() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage4() at System.ServiceModel.Dispatcher.MessageRpc.Process() at System.ServiceModel.Dispatcher.ChannelHandler.DispatchAndReleasePump() at System.ServiceModel.Dispatcher.ChannelHandler.HandleRequest() at System.ServiceModel.Dispatcher.ChannelHandler.AsyncMessagePump() at System.ServiceModel.Diagnostics.Utility+AsyncThunk.UnhandledExceptionFrame() at System.ServiceModel.AsyncResult.Complete() at System.ServiceModel.Channels.InputQueue`1+AsyncQueueReader.Set() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueueChannel`1.EnqueueAndDispatch() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.HttpChannelListener.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpTransportManager.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.BeginRequest() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequest() at System.ServiceModel.PartialTrustHelpers.PartialTrustInvoke() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequestWithFlow() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke2() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.ProcessCallbacks() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.CompletionCallback() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+ScheduledOverlapped.IOCallback() at System.ServiceModel.Diagnostics.Utility+IOCompletionThunk.UnhandledExceptionFrame() at System.Threading._IOCompletionCallback.PerformIOCompletionCallback() "
                                //}, {
                                //  "id" : 171889775,
                                //  "version" : 0,
                                //  "name" : "System.Messaging.MessageQueueException",
                                //  "value" : "Insufficient resources to perform operation.AD_STACK_TRACE:System.Messaging.MessageQueueException: at System.Messaging.MessageQueue.SendInternal() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Messaging.Sender.Send() at Corillian.Platform.Audit.AuditTrxSender.Audit() at USB.DigitalServices.Audit.MessageReceiver.Process() at USB.DigitalServices.HandlerCore.ContextSafeHandler`1.Audit() at USB.DCISService.Accounts.V1.Handlers.GetAccountTransactionsV4.Execute() at Fiserv.AppService.Core.HandlerBase`1.Execute() at Fiserv.AppService.Core.ServiceProcessor.Process() at USB.DCIS.Server.DCISServiceServer.Execute() at .SyncInvokeExecute() at System.ServiceModel.Dispatcher.SyncMethodInvoker.Invoke() at System.ServiceModel.Dispatcher.DispatchOperationRuntime.InvokeBegin() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage5() at System.ServiceModel.Dispatcher.ImmutableDispatchRuntime.ProcessMessage4() at System.ServiceModel.Dispatcher.MessageRpc.Process() at System.ServiceModel.Dispatcher.ChannelHandler.DispatchAndReleasePump() at System.ServiceModel.Dispatcher.ChannelHandler.HandleRequest() at System.ServiceModel.Dispatcher.ChannelHandler.AsyncMessagePump() at System.ServiceModel.Diagnostics.Utility+AsyncThunk.UnhandledExceptionFrame() at System.ServiceModel.AsyncResult.Complete() at System.ServiceModel.Channels.InputQueue`1+AsyncQueueReader.Set() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueue`1.EnqueueAndDispatch() at System.ServiceModel.Channels.InputQueueChannel`1.EnqueueAndDispatch() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.SingletonChannelAcceptor`3.Enqueue() at System.ServiceModel.Channels.HttpChannelListener.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpTransportManager.HttpContextReceived() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.BeginRequest() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequest() at System.ServiceModel.PartialTrustHelpers.PartialTrustInvoke() at System.ServiceModel.Activation.HostedHttpRequestAsyncResult.OnBeginRequestWithFlow() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke2() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+WorkItem.Invoke() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.ProcessCallbacks() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper.CompletionCallback() at System.ServiceModel.Channels.IOThreadScheduler+CriticalHelper+ScheduledOverlapped.IOCallback() at System.ServiceModel.Diagnostics.Utility+IOCompletionThunk.UnhandledExceptionFrame() at System.Threading._IOCompletionCallback.PerformIOCompletionCallback() "
                                //}, {
                                //  "id" : 171889775,
                                //  "version" : 0,
                                //  "name" : "1. USB.OLBService.Handlers.TransactionUtilities",
                                //  "value" : "USB.OLBService.Handlers.TransactionUtilities : Error occurred in MapHostTransactions: System.NullReferenceException: Object reference not set to an instance of an object.\r\n   at USB.OLBService.Handlers.TransactionUtilities.MapCheckCardHostResponseTransactions(GetOutStandingAuthRequest requestFromUI, List`1 transactions, USBAccount actualAcct)"
                                //} ]

                                #endregion

                                foreach (DetectedError detectedError in detectedErrorsListInThisSegment)
                                {
                                    // Try by exact message match
                                    DetectedError detectedErrorWithErrorID = detectedErrorsFromErrorIDs.Where(e => e.ErrorName == detectedError.ErrorMessage).FirstOrDefault();

                                    // Try starting with the message
                                    if (detectedErrorWithErrorID == null)
                                    {
                                        detectedErrorWithErrorID = detectedErrorsFromErrorIDs.Where(e => e.ErrorName.StartsWith(detectedError.ErrorMessage)).FirstOrDefault();
                                    }

                                    // Try containing the message
                                    if (detectedErrorWithErrorID == null)
                                    {
                                        detectedErrorWithErrorID = detectedErrorsFromErrorIDs.Where(e => e.ErrorName.Contains(detectedError.ErrorMessage)).FirstOrDefault();
                                    }

                                    // Try by partial name match second
                                    if (detectedErrorWithErrorID == null)
                                    {
                                        // Split by . and :
                                        // java.io.IOException 
                                        //      -> java, io, IOException
                                        //      Detected as IOException
                                        // Corillian.Voyager.ExecutionServices.Client.TRBException:Corillian.Voyager.ExecutionServices.Client.TRBException
                                        //      -> Corillian, Voyager, ExecutionServices, Client, TRBException, Corillian, Voyager, ExecutionServices, Client, TRBException
                                        //      Detected as TRBException
                                        string[] errorMessageTokens = detectedError.ErrorMessage.Split('.', ':');

                                        // Go backwards because exception type is at the end
                                        for (int i = errorMessageTokens.Length - 1; i >= 0; i--)
                                        {
                                            detectedErrorWithErrorID = detectedErrorsFromErrorIDs.Where(e => e.ErrorName.Contains(errorMessageTokens[i])).FirstOrDefault();
                                            if (detectedErrorWithErrorID != null)
                                            {
                                                break;
                                            }
                                        }
                                    }

                                    // Did we find it?
                                    if (detectedErrorWithErrorID != null)
                                    {
                                        // Yay, we did, mark this error by ID off as matched and copy the values to the final 
                                        detectedErrorWithErrorID.ErrorIDMatchedToMessage = true;

                                        detectedError.ErrorID = detectedErrorWithErrorID.ErrorID;
                                        detectedError.ErrorName = detectedErrorWithErrorID.ErrorName;
                                        detectedError.ErrorType = detectedErrorWithErrorID.ErrorType;

                                        #region Fill in the deeplinks for the Error

                                        detectedError.ControllerLink = segment.Controller;
                                        detectedError.ApplicationLink = segment.ApplicationLink;
                                        detectedError.TierLink = segment.TierLink;
                                        detectedError.NodeLink = segment.NodeLink;
                                        detectedError.BTLink = segment.BTLink;
                                        detectedError.ErrorLink = String.Format(DEEPLINK_ERROR, detectedError.Controller, detectedError.ApplicationID, detectedError.ErrorID, DEEPLINK_THIS_TIMERANGE);

                                        #endregion

                                    }
                                }

                                // At this point, we matched what we could.
                                // A little cleanup - what if we have 1 by error ID and 1 message without Error ID left? If yes, those obviously match
                                List<DetectedError> detectedErrorsFromErrorIDsUnmatched = detectedErrorsFromErrorIDs.Where(e => e.ErrorIDMatchedToMessage == false).ToList();
                                List<DetectedError> detectedErrorsListInThisSegmentUnmatched = detectedErrorsListInThisSegment.Where(e => e.ErrorID == -1).ToList();
                                if (detectedErrorsFromErrorIDsUnmatched.Count == 1 && detectedErrorsListInThisSegmentUnmatched.Count == 1)
                                {
                                    DetectedError detectedError = detectedErrorsListInThisSegmentUnmatched[0];
                                    DetectedError detectedErrorWithErrorID = detectedErrorsFromErrorIDsUnmatched[0];
                                    detectedErrorWithErrorID.ErrorIDMatchedToMessage = true;

                                    detectedError.ErrorID = detectedErrorWithErrorID.ErrorID;
                                    detectedError.ErrorName = detectedErrorWithErrorID.ErrorName;
                                    detectedError.ErrorType = detectedErrorWithErrorID.ErrorType;

                                    #region Fill in the deeplinks for the Error

                                    detectedError.ControllerLink = segment.Controller;
                                    detectedError.ApplicationLink = segment.ApplicationLink;
                                    detectedError.TierLink = segment.TierLink;
                                    detectedError.NodeLink = segment.NodeLink;
                                    detectedError.BTLink = segment.BTLink;
                                    detectedError.ErrorLink = String.Format(DEEPLINK_ERROR, detectedError.Controller, detectedError.ApplicationID, detectedError.ErrorID, DEEPLINK_THIS_TIMERANGE);

                                    #endregion
                                }
                                else if (detectedErrorsFromErrorIDsUnmatched.Count > 1)
                                {
                                    // Don't know how to match those. Let's just append them to the list
                                    detectedErrorsListInThisSegment.AddRange(detectedErrorsFromErrorIDsUnmatched);
                                }
                            }

                            #endregion

                            #region Process Data Collectors in Segment

                            List<BusinessData> businessDataListInThisSegment = new List<BusinessData>();

                            // Transaction properties
                            foreach (JToken transactionPropertyToken in snapshotSegmentDetail["transactionProperties"])
                            {
                                BusinessData businessData = new BusinessData();

                                businessData.Controller = segment.Controller;
                                businessData.ApplicationName = segment.ApplicationName;
                                businessData.ApplicationID = segment.ApplicationID;
                                businessData.TierID = segment.TierID;
                                businessData.TierName = segment.TierName;
                                businessData.BTID = segment.BTID;
                                businessData.BTName = segment.BTName;
                                businessData.NodeID = segment.NodeID;
                                businessData.NodeName = segment.NodeName;

                                businessData.RequestID = segment.RequestID;
                                businessData.SegmentID = segment.SegmentID;

                                businessData.DataType = "Transaction";

                                businessData.DataName = transactionPropertyToken["name"].ToString();
                                businessData.DataValue = transactionPropertyToken["value"].ToString();

                                #region Fill in the deeplinks for the Business Data

                                businessData.ControllerLink = segment.Controller;
                                businessData.ApplicationLink = segment.ApplicationLink;
                                businessData.TierLink = segment.TierLink;
                                businessData.NodeLink = segment.NodeLink;
                                businessData.BTLink = segment.BTLink;

                                #endregion

                                businessDataListInThisSegment.Add(businessData);
                            }

                            // HTTP data collectors
                            foreach (JToken transactionPropertyToken in snapshotSegmentDetail["httpParameters"])
                            {
                                BusinessData businessData = new BusinessData();

                                businessData.Controller = segment.Controller;
                                businessData.ApplicationName = segment.ApplicationName;
                                businessData.ApplicationID = segment.ApplicationID;
                                businessData.TierID = segment.TierID;
                                businessData.TierName = segment.TierName;
                                businessData.BTID = segment.BTID;
                                businessData.BTName = segment.BTName;
                                businessData.NodeID = segment.NodeID;
                                businessData.NodeName = segment.NodeName;

                                businessData.RequestID = segment.RequestID;
                                businessData.SegmentID = segment.SegmentID;

                                businessData.DataType = "HTTP";

                                businessData.DataName = transactionPropertyToken["name"].ToString();
                                businessData.DataValue = transactionPropertyToken["value"].ToString();

                                #region Fill in the deeplinks for the Business Data

                                businessData.ControllerLink = segment.Controller;
                                businessData.ApplicationLink = segment.ApplicationLink;
                                businessData.TierLink = segment.TierLink;
                                businessData.NodeLink = segment.NodeLink;
                                businessData.BTLink = segment.BTLink;

                                #endregion

                                businessDataListInThisSegment.Add(businessData);
                            }

                            // MIDCs 
                            foreach (JToken transactionPropertyToken in snapshotSegmentDetail["businessData"])
                            {
                                BusinessData businessData = new BusinessData();

                                businessData.Controller = segment.Controller;
                                businessData.ApplicationName = segment.ApplicationName;
                                businessData.ApplicationID = segment.ApplicationID;
                                businessData.TierID = segment.TierID;
                                businessData.TierName = segment.TierName;
                                businessData.BTID = segment.BTID;
                                businessData.BTName = segment.BTName;
                                businessData.NodeID = segment.NodeID;
                                businessData.NodeName = segment.NodeName;

                                businessData.RequestID = segment.RequestID;
                                businessData.SegmentID = segment.SegmentID;

                                businessData.DataType = "Code";

                                businessData.DataName = transactionPropertyToken["name"].ToString();
                                businessData.DataValue = transactionPropertyToken["value"].ToString().Trim('[', ']');

                                #region Fill in the deeplinks for the Business Data

                                businessData.ControllerLink = segment.Controller;
                                businessData.ApplicationLink = segment.ApplicationLink;
                                businessData.TierLink = segment.TierLink;
                                businessData.NodeLink = segment.NodeLink;
                                businessData.BTLink = segment.BTLink;

                                #endregion

                                businessDataListInThisSegment.Add(businessData);
                            }

                            #endregion

                            #region Update call chains and call types from exits into segment

                            SortedDictionary<string, int> callChainsSegment = new SortedDictionary<string, int>();
                            SortedDictionary<string, int> exitTypesSegment = new SortedDictionary<string, int>();

                            foreach (ExitCall exitCall in exitCallsListInThisSegment)
                            {
                                string callChain = exitCall.CallChain;
                                if (callChainsSegment.ContainsKey(callChain) == true)
                                {
                                    callChainsSegment[callChain]++;
                                }
                                else
                                {
                                    callChainsSegment.Add(callChain, 1);
                                }
                                if (callChainsSnapshot.ContainsKey(callChain) == true)
                                {
                                    callChainsSnapshot[callChain]++;
                                }
                                else
                                {
                                    callChainsSnapshot.Add(callChain, 1);
                                }
                                string exitType = exitCall.ExitType;
                                if (exitTypesSegment.ContainsKey(exitType) == true)
                                {
                                    exitTypesSegment[exitType]++;
                                }
                                else
                                {
                                    exitTypesSegment.Add(exitType, 1);
                                }
                                if (exitTypesSnapshot.ContainsKey(exitType) == true)
                                {
                                    exitTypesSnapshot[exitType]++;
                                }
                                else
                                {
                                    exitTypesSnapshot.Add(exitType, 1);
                                }
                            }
                            if (exitCallsListInThisSegment.Count == 0)
                            {
                                string callChain = String.Format("{0}->(self)", callChainForThisSegment);
                                if (callChainsSegment.ContainsKey(callChain) == true)
                                {
                                    callChainsSegment[callChain]++;
                                }
                                else
                                {
                                    callChainsSegment.Add(callChain, 1);
                                }
                                if (callChainsSnapshot.ContainsKey(callChain) == true)
                                {
                                    callChainsSnapshot[callChain]++;
                                }
                                else
                                {
                                    callChainsSnapshot.Add(callChain, 1);
                                }
                            }

                            StringBuilder sb1 = new StringBuilder(128 * callChainsSegment.Count);
                            foreach (var callChain in callChainsSegment)
                            {
                                sb1.AppendFormat("{0}:[{1}];\n", callChain.Key, callChain.Value);
                            }
                            if (sb1.Length > 2) { sb1.Remove(sb1.Length - 2, 2); }
                            segment.CallChains = sb1.ToString();

                            sb1 = new StringBuilder(10 * exitTypesSegment.Count);
                            foreach (var exitType in exitTypesSegment)
                            {
                                sb1.AppendFormat("{0}:[{1}];\n", exitType.Key, exitType.Value);
                            }
                            if (sb1.Length > 2) { sb1.Remove(sb1.Length - 2, 2); }
                            segment.ExitTypes = sb1.ToString();

                            #endregion

                            #region Update counts of calls and types of destinations for Segment

                            segment.NumCallsToTiers = exitCallsListInThisSegment.Where(e => e.ToEntityType == "Tier").Sum(e => e.NumCalls);
                            segment.NumCallsToBackends = exitCallsListInThisSegment.Where(e => e.ToEntityType == "Backend").Sum(e => e.NumCalls);
                            segment.NumCallsToApplications = exitCallsListInThisSegment.Where(e => e.ToEntityType == "Application").Sum(e => e.NumCalls);

                            segment.NumCalledTiers = exitCallsListInThisSegment.Where(e => e.ToEntityType == "Tier").GroupBy(e => e.ToEntityName).Count();
                            segment.NumCalledBackends = exitCallsListInThisSegment.Where(e => e.ToEntityType == "Backend").GroupBy(e => e.ToEntityName).Count();
                            segment.NumCalledApplications = exitCallsListInThisSegment.Where(e => e.ToEntityType == "Application").GroupBy(e => e.ToEntityName).Count();

                            segment.NumSEPs = serviceEndpointCallsListInThisSegment.Count();

                            segment.NumHTTPDCs = businessDataList.Where(d => d.DataType == "HTTP").Count();
                            segment.NumMIDCs = businessDataList.Where(d => d.DataType == "Code").Count();

                            #endregion

                            // Add the created entities
                            segmentsList.Add(segment);
                            exitCallsList.AddRange(exitCallsListInThisSegment);
                            serviceEndpointCallsList.AddRange(serviceEndpointCallsListInThisSegment);
                            detectedErrorsList.AddRange(detectedErrorsListInThisSegment);
                            businessDataList.AddRange(businessDataListInThisSegment);
                        }
                    }

                    // Sort things prettily
                    segmentsList = segmentsList.OrderByDescending(s => s.IsFirstInChain).ThenBy(s => s.Occured).ThenBy(s => s.UserExperience).ToList();
                    exitCallsList = exitCallsList.OrderBy(c => c.RequestID).ThenBy(c => c.SegmentID).ThenBy(c => c.ExitType).ToList();
                    serviceEndpointCallsList = serviceEndpointCallsList.OrderBy(s => s.RequestID).ThenBy(s => s.SegmentID).ThenBy(s => s.SEPName).ToList();
                    detectedErrorsList = detectedErrorsList.OrderBy(e => e.RequestID).ThenBy(e => e.SegmentID).ThenBy(e => e.ErrorName).ToList();
                    businessDataList = businessDataList.OrderBy(b => b.DataType).ThenBy(b => b.DataName).ToList();

                    #region Update call chains from segments into snapshot

                    StringBuilder sb = new StringBuilder(128 * callChainsSnapshot.Count);
                    foreach (var callChain in callChainsSnapshot)
                    {
                        sb.AppendFormat("{0};\n", callChain.Key);
                    }
                    if (sb.Length > 2) { sb.Remove(sb.Length - 2, 2); }
                    snapshot.CallChains = sb.ToString();

                    sb = new StringBuilder(10 * exitTypesSnapshot.Count);
                    foreach (var exitType in exitTypesSnapshot)
                    {
                        sb.AppendFormat("{0}:[{1}];\n", exitType.Key, exitType.Value);
                    }
                    if (sb.Length > 2) { sb.Remove(sb.Length - 2, 2); }
                    snapshot.ExitTypes = sb.ToString();

                    #endregion

                    #region Update various counts for Snapshot columns

                    snapshot.NumErrors = segmentsList.Sum(s => s.NumErrors);

                    snapshot.NumSegments = segmentsList.Count;
                    snapshot.NumCallGraphs = segmentsList.Count(s => s.CallGraphType != "NONE");

                    snapshot.NumCallsToTiers = segmentsList.Sum(s => s.NumCallsToTiers);
                    snapshot.NumCallsToBackends = segmentsList.Sum(s => s.NumCallsToBackends);
                    snapshot.NumCallsToApplications = segmentsList.Sum(s => s.NumCallsToApplications);

                    snapshot.NumCalledTiers = segmentsList.Sum(s => s.NumCalledTiers);
                    snapshot.NumCalledBackends = segmentsList.Sum(s => s.NumCalledBackends);
                    snapshot.NumCalledApplications = segmentsList.Sum(s => s.NumCalledApplications);

                    snapshot.NumSEPs = segmentsList.Sum(s => s.NumSEPs);

                    snapshot.NumHTTPDCs = segmentsList.Sum(s => s.NumHTTPDCs);
                    snapshot.NumMIDCs = segmentsList.Sum(s => s.NumMIDCs);

                    #endregion
                }

                #endregion

                #region Save results

                // Save results
                if (segmentsList != null)
                {
                    FileIOHelper.writeListToCSVFile(segmentsList, new SegmentReportMap(), segmentsFileName);
                }

                if (exitCallsList != null)
                {
                    FileIOHelper.writeListToCSVFile(exitCallsList, new ExitCallReportMap(), exitCallsFileName);
                }

                if (serviceEndpointCallsList != null)
                {

                    FileIOHelper.writeListToCSVFile(serviceEndpointCallsList, new ServiceEndpointCallReportMap(), serviceEndpointCallsFileName);
                }

                if (detectedErrorsList != null)
                {
                    FileIOHelper.writeListToCSVFile(detectedErrorsList, new DetectedErrorReportMap(), detectedErrorsFileName);
                }

                if (businessDataList != null)
                {
                    FileIOHelper.writeListToCSVFile(businessDataList, new BusinessDataReportMap(), businessDataFileName);
                }

                List<Snapshot> snapshotRows = new List<Snapshot>(1);
                snapshotRows.Add(snapshot);
                FileIOHelper.writeListToCSVFile(snapshotRows, new SnapshotReportMap(), snapshotsFileName);

                #endregion

                if (progressToConsole == true)
                {
                    j++;
                    if (j % 100 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        #endregion


        #region Entity report functions

        private static void adjustColumnsOfEntityRowTableInEntitiesReport(string entityType, ExcelWorksheet sheet, ExcelTable table)
        {
            if (entityType == APPLICATION_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
            }
            else if (entityType == TIERS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["AgentType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
            }
            else if (entityType == NODES_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ApplicationName"].Position + 1).AutoFit();
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["AgentType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["AgentVersion"].Position + 1).AutoFit();
                sheet.Column(table.Columns["MachineName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["MachineAgentVersion"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["NodeLink"].Position + 1).AutoFit();
            }
            else if (entityType == BACKENDS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ApplicationName"].Position + 1).AutoFit();
                sheet.Column(table.Columns["BackendName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["BackendType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["Prop1Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["Prop2Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["Prop3Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["Prop4Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["Prop5Name"].Position + 1).AutoFit();
                sheet.Column(table.Columns["Prop1Value"].Position + 1).Width = 20;
                sheet.Column(table.Columns["Prop2Value"].Position + 1).Width = 20;
                sheet.Column(table.Columns["Prop3Value"].Position + 1).Width = 20;
                sheet.Column(table.Columns["Prop4Value"].Position + 1).Width = 20;
                sheet.Column(table.Columns["Prop5Value"].Position + 1).Width = 20;
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["BackendLink"].Position + 1).AutoFit();
            }
            else if (entityType == BUSINESS_TRANSACTIONS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ApplicationName"].Position + 1).AutoFit();
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["BTType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["BTLink"].Position + 1).AutoFit();
            }
            else if (entityType == SERVICE_ENDPOINTS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ApplicationName"].Position + 1).AutoFit();
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["SEPName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["SEPType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["SEPLink"].Position + 1).AutoFit();
            }
            else if (entityType == ERRORS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ApplicationName"].Position + 1).AutoFit();
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ErrorName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ErrorType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["HttpCode"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ErrorDepth"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ErrorLevel1"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ErrorLevel2"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ErrorLevel3"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ErrorLevel4"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ErrorLevel5"].Position + 1).Width = 20;
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ErrorLink"].Position + 1).AutoFit();
            }
        }

        #endregion


        #region Entity metric detail report functions

        private static int reportMetricDetailApplication(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, EntityApplication applicationRow)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            ExcelPackage excelEntitiesDetail = createIndividualEntityMetricReportTemplate(programOptions, jobConfiguration, jobTarget);
            fillIndividualEntityMetricReportForEntity(programOptions, jobConfiguration, jobTarget, excelEntitiesDetail, APPLICATION_TYPE_SHORT, applicationRow);

            // Report file
            string reportFilePath = getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, applicationRow);

            finalizeAndSaveIndividualEntityMetricReport(excelEntitiesDetail, reportFilePath);

            return 1;
        }

        private static int reportMetricDetailTiers(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityTier> entityList, bool progressToConsole)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            int j = 0;

            foreach (EntityTier tierRow in entityList)
            {
                //string metricsEntityFolderPath = Path.Combine(
                //    metricsFolderPath,
                //    TIERS_TYPE_SHORT,
                //    getShortenedEntityNameForFileSystem(tierRow.TierName, tierRow.TierID));

                ExcelPackage excelEntitiesDetail = createIndividualEntityMetricReportTemplate(programOptions, jobConfiguration, jobTarget);
                fillIndividualEntityMetricReportForEntity(programOptions, jobConfiguration, jobTarget, excelEntitiesDetail, TIERS_TYPE_SHORT, tierRow);

                // Report file
                string reportFilePath = getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, tierRow);

                finalizeAndSaveIndividualEntityMetricReport(excelEntitiesDetail, reportFilePath);
                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int reportMetricDetailNodes(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityNode> entityList, bool progressToConsole)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            int j = 0;

            foreach (EntityNode nodeRow in entityList)
            {
                ExcelPackage excelEntitiesDetail = createIndividualEntityMetricReportTemplate(programOptions, jobConfiguration, jobTarget);
                fillIndividualEntityMetricReportForEntity(programOptions, jobConfiguration, jobTarget, excelEntitiesDetail, NODES_TYPE_SHORT, nodeRow);

                // Report file
                string reportFilePath = getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, nodeRow);

                finalizeAndSaveIndividualEntityMetricReport(excelEntitiesDetail, reportFilePath);
                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int reportMetricDetailBackends(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityBackend> entityList, bool progressToConsole)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            int j = 0;

            foreach (EntityBackend backendRow in entityList)
            {
                ExcelPackage excelEntitiesDetail = createIndividualEntityMetricReportTemplate(programOptions, jobConfiguration, jobTarget);
                fillIndividualEntityMetricReportForEntity(programOptions, jobConfiguration, jobTarget, excelEntitiesDetail, BACKENDS_TYPE_SHORT, backendRow);

                // Report file
                string reportFilePath = getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, backendRow);

                finalizeAndSaveIndividualEntityMetricReport(excelEntitiesDetail, reportFilePath);
                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int reportMetricDetailBusinessTransactions(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityBusinessTransaction> entityList, bool progressToConsole)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            int j = 0;

            foreach (EntityBusinessTransaction businessTransactionRow in entityList)
            {
                ExcelPackage excelEntitiesDetail = createIndividualEntityMetricReportTemplate(programOptions, jobConfiguration, jobTarget);
                fillIndividualEntityMetricReportForEntity(programOptions, jobConfiguration, jobTarget, excelEntitiesDetail, BUSINESS_TRANSACTIONS_TYPE_SHORT, businessTransactionRow);

                // Report file
                string reportFilePath = getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, businessTransactionRow);

                finalizeAndSaveIndividualEntityMetricReport(excelEntitiesDetail, reportFilePath);
                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int reportMetricDetailServiceEndpoints(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityServiceEndpoint> entityList, bool progressToConsole)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            int j = 0;

            foreach (EntityServiceEndpoint serviceEndpointRow in entityList)
            {
                ExcelPackage excelEntitiesDetail = createIndividualEntityMetricReportTemplate(programOptions, jobConfiguration, jobTarget);
                fillIndividualEntityMetricReportForEntity(programOptions, jobConfiguration, jobTarget, excelEntitiesDetail, SERVICE_ENDPOINTS_TYPE_SHORT, serviceEndpointRow);

                // Report file
                string reportFilePath = getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, serviceEndpointRow);

                finalizeAndSaveIndividualEntityMetricReport(excelEntitiesDetail, reportFilePath);
                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static int reportMetricDetailErrors(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, List<EntityError> entityList, bool progressToConsole)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string reportsFolderPath = Path.Combine(applicationFolderPath, REPORTS_FOLDER_NAME);

            #endregion

            int j = 0;

            foreach (EntityError errorRow in entityList)
            {
                ExcelPackage excelEntitiesDetail = createIndividualEntityMetricReportTemplate(programOptions, jobConfiguration, jobTarget);
                fillIndividualEntityMetricReportForEntity(programOptions, jobConfiguration, jobTarget, excelEntitiesDetail, ERRORS_TYPE_SHORT, errorRow);

                // Report file
                string reportFilePath = getEntityMetricReportFilePath(programOptions, jobConfiguration, jobTarget, reportsFolderPath, errorRow);

                finalizeAndSaveIndividualEntityMetricReport(excelEntitiesDetail, reportFilePath);
                if (progressToConsole == true)
                {
                    j++;
                    if (j % 10 == 0)
                    {
                        Console.Write("[{0}].", j);
                    }
                }
            }

            return entityList.Count;
        }

        private static string getEntityMetricReportFilePath(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, string reportsFolderPath, EntityBase entityRow)
        {
            string reportFileName = String.Empty;
            string reportFilePath = String.Empty;

            if (entityRow is EntityApplication)
            {
                reportFileName = String.Format(
                    REPORT_ENTITY_DETAILS_APPLICATION_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(entityRow.Controller).Host),
                    getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID));
                reportFilePath = Path.Combine(
                    reportsFolderPath,
                    APPLICATION_TYPE_SHORT,
                    reportFileName);
            }
            else if (entityRow is EntityTier)
            {
                EntityTier tierRow = (EntityTier)entityRow;
                reportFileName = String.Format(
                    REPORT_ENTITY_DETAILS_ENTITY_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(entityRow.Controller).Host),
                    getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID),
                    getShortenedEntityNameForFileSystem(tierRow.TierName, tierRow.TierID));
                reportFilePath = Path.Combine(
                    reportsFolderPath,
                    TIERS_TYPE_SHORT,
                    reportFileName);
            }
            else if (entityRow is EntityNode)
            {
                EntityNode nodeRow = (EntityNode)entityRow;
                reportFileName = String.Format(
                    REPORT_ENTITY_DETAILS_ENTITY_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(entityRow.Controller).Host),
                    getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID),
                    getShortenedEntityNameForFileSystem(nodeRow.NodeName, nodeRow.NodeID));
                reportFilePath = Path.Combine(
                    reportsFolderPath,
                    NODES_TYPE_SHORT,
                    reportFileName);
            }
            else if (entityRow is EntityBackend)
            {
                EntityBackend backendRow = (EntityBackend)entityRow;
                reportFileName = String.Format(
                    REPORT_ENTITY_DETAILS_ENTITY_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(entityRow.Controller).Host),
                    getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID),
                    getShortenedEntityNameForFileSystem(backendRow.BackendName, backendRow.BackendID));
                reportFilePath = Path.Combine(
                    reportsFolderPath,
                    BACKENDS_TYPE_SHORT,
                    reportFileName);
            }
            else if (entityRow is EntityBusinessTransaction)
            {
                EntityBusinessTransaction businessTransactionRow = (EntityBusinessTransaction)entityRow;
                reportFileName = String.Format(
                    REPORT_ENTITY_DETAILS_ENTITY_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(entityRow.Controller).Host),
                    getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID),
                    getShortenedEntityNameForFileSystem(businessTransactionRow.BTName, businessTransactionRow.BTID));
                reportFilePath = Path.Combine(
                    reportsFolderPath,
                    BUSINESS_TRANSACTIONS_TYPE_SHORT,
                    reportFileName);
            }
            else if (entityRow is EntityServiceEndpoint)
            {
                EntityServiceEndpoint serviceEndpointRow = (EntityServiceEndpoint)entityRow;
                reportFileName = String.Format(
                    REPORT_ENTITY_DETAILS_ENTITY_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(entityRow.Controller).Host),
                    getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID),
                    getShortenedEntityNameForFileSystem(serviceEndpointRow.SEPName, serviceEndpointRow.SEPID));
                reportFilePath = Path.Combine(
                    reportsFolderPath,
                    SERVICE_ENDPOINTS_TYPE_SHORT,
                    reportFileName);
            }
            else if (entityRow is EntityError)
            {
                EntityError errorRow = (EntityError)entityRow;
                reportFileName = String.Format(
                    REPORT_ENTITY_DETAILS_ENTITY_FILE_NAME,
                    programOptions.JobName,
                    jobConfiguration.Input.ExpandedTimeRange.From,
                    jobConfiguration.Input.ExpandedTimeRange.To,
                    getFileSystemSafeString(new Uri(entityRow.Controller).Host),
                    getShortenedEntityNameForFileSystem(entityRow.ApplicationName, entityRow.ApplicationID),
                    getShortenedEntityNameForFileSystem(errorRow.ErrorName, errorRow.ErrorID));
                reportFilePath = Path.Combine(
                    reportsFolderPath,
                    ERRORS_TYPE_SHORT,
                    reportFileName);
            }

            return reportFilePath;
        }

        private static ExcelPackage createIndividualEntityMetricReportTemplate(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget)
        {
            #region Target step variables

            // Various folders
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));

            // Report files
            string controllerReportFilePath = Path.Combine(controllerFolderPath, CONVERT_ENTITY_CONTROLLER_FILE_NAME);

            #endregion

            #region Prepare the report package

            // Prepare package
            ExcelPackage excelEntityDetail = new ExcelPackage();
            excelEntityDetail.Workbook.Properties.Author = String.Format("AppDynamics DEXTER {0}", Assembly.GetEntryAssembly().GetName().Version);
            excelEntityDetail.Workbook.Properties.Title = "AppDynamics DEXTER Entity Detail Report";
            excelEntityDetail.Workbook.Properties.Subject = programOptions.JobName;

            excelEntityDetail.Workbook.Properties.Comments = String.Format("Targets={0}\nFrom={1:o}\nTo={2:o}", jobConfiguration.Target.Count, jobConfiguration.Input.TimeRange.From, jobConfiguration.Input.TimeRange.To);

            #endregion

            #region Parameters sheet

            // Parameters sheet
            ExcelWorksheet sheet = excelEntityDetail.Workbook.Worksheets.Add(REPORT_SHEET_PARAMETERS);

            var hyperLinkStyle = sheet.Workbook.Styles.CreateNamedStyle("HyperLinkStyle");
            hyperLinkStyle.Style.Font.UnderLineType = ExcelUnderLineType.Single;
            hyperLinkStyle.Style.Font.Color.SetColor(Color.Blue);

            int l = 1;
            sheet.Cells[l, 1].Value = "Table of Contents";
            sheet.Cells[l, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
            l++; l++;
            sheet.Cells[l, 1].Value = "AppDynamics DEXTER Entities Detail Report";
            l++; l++;
            sheet.Cells[l, 1].Value = "From";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.From.ToString("G");
            l++;
            sheet.Cells[l, 1].Value = "To";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.TimeRange.To.ToString("G");
            l++;
            sheet.Cells[l, 1].Value = "Expanded From (UTC)";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToString("G");
            l++;
            sheet.Cells[l, 1].Value = "Expanded From (Local)";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.From.ToLocalTime().ToString("G");
            l++;
            sheet.Cells[l, 1].Value = "Expanded To (UTC)";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToString("G");
            l++;
            sheet.Cells[l, 1].Value = "Expanded To (Local)";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.ExpandedTimeRange.To.ToLocalTime().ToString("G");
            l++;
            sheet.Cells[l, 1].Value = "Number of Hours Intervals";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.HourlyTimeRanges.Count;
            l++;
            sheet.Cells[l, 1].Value = "Export Metrics";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.Metrics;
            l++;
            sheet.Cells[l, 1].Value = "Export Snapshots";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.Snapshots;
            l++;
            sheet.Cells[l, 1].Value = "Export Flowmaps";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.Flowmaps;
            l++;
            sheet.Cells[l, 1].Value = "Export Configuration";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.Configuration;
            l++;
            sheet.Cells[l, 1].Value = "Export Events";
            sheet.Cells[l, 2].Value = jobConfiguration.Input.Events;
            l++;
            sheet.Cells[l, 1].Value = "Target:";
            l++; l++;
            sheet.Cells[l, 1].Value = "Controller";
            sheet.Cells[l, 2].Value = jobTarget.Controller;
            l++; 
            sheet.Cells[l, 1].Value = "UserName";
            sheet.Cells[l, 2].Value = jobTarget.Controller;
            l++;
            sheet.Cells[l, 1].Value = "Application";
            sheet.Cells[l, 2].Value = jobTarget.Application;
            l++;
            sheet.Cells[l, 1].Value = "ID";
            sheet.Cells[l, 2].Value = jobTarget.ApplicationID;
            l++;
            sheet.Cells[l, 1].Value = "Status";
            sheet.Cells[l, 2].Value = jobTarget.Status;

            sheet.Column(1).AutoFit();
            sheet.Column(2).AutoFit();

            #endregion

            #region TOC sheet

            // Navigation sheet with link to other sheets
            sheet = excelEntityDetail.Workbook.Worksheets.Add(REPORT_SHEET_TOC);

            #endregion

            #region Controller sheet

            sheet = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_CONTROLLERS_LIST);
            sheet.Cells[1, 1].Value = "Table of Contents";
            sheet.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
            sheet.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

            ExcelRangeBase range = readCSVFileIntoExcelRange(controllerReportFilePath, 0, sheet, REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT, 1);
            if (range != null)
            {
                ExcelTable table = sheet.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_CONTROLLERS);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheet.Column(table.Columns["Controller"].Position + 1).AutoFit();
                sheet.Column(table.Columns["UserName"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
            }

            #endregion

            return excelEntityDetail;
        }

        private static void fillIndividualEntityMetricReportForEntity(ProgramOptions programOptions, JobConfiguration jobConfiguration, JobTarget jobTarget, ExcelPackage excelEntityDetail, string entityType, EntityBase entityRow)
        {
            #region Target step variables

            // Various folders and files
            string controllerFolderPath = Path.Combine(programOptions.OutputJobFolderPath, getFileSystemSafeString(new Uri(jobTarget.Controller).Host));
            string applicationFolderPath = Path.Combine(controllerFolderPath, getShortenedEntityNameForFileSystem(jobTarget.Application, jobTarget.ApplicationID));
            string entitiesFolderPath = Path.Combine(applicationFolderPath, ENTITIES_FOLDER_NAME);
            string eventsFolderPath = Path.Combine(applicationFolderPath, EVENTS_FOLDER_NAME);
            string snapshotsFolderPath = Path.Combine(applicationFolderPath, SNAPSHOTS_FOLDER_NAME);
            string metricsFolderPath = Path.Combine(applicationFolderPath, METRICS_FOLDER_NAME);
            string metricsEntityFolderTypePath = Path.Combine(metricsFolderPath, entityType);
            string metricsEntityFolderPath = String.Empty;

            // Metric paths and files
            string entityFullRangeReportFilePath = String.Empty;
            string entityHourlyRangeReportFilePath = String.Empty;
            string metricsDataFolderPath = String.Empty;
            string entityMetricSummaryReportFilePath = String.Empty;
            string entityMetricValuesReportFilePath = String.Empty;
            string entityName = String.Empty;
            long entityID = -1;
            string entityNameForDisplay = String.Empty;
            string entityTypeForDisplay = String.Empty;
            int fromRow = 1;

            // Report tables and ranges
            ExcelRangeBase range;
            ExcelTable table;

            ExcelTable tableValuesART = null;
            ExcelTable tableValuesCPM = null;
            ExcelTable tableValuesEPM = null;
            ExcelTable tableValuesEXCPM = null;
            ExcelTable tableValuesHTTPEPM = null;

            ExcelTable tableEvents = null;
            ExcelTable tableSnapshots = null;

            #endregion

            #region Fill sheet and table name variables based on entity type

            if (entityType == APPLICATION_TYPE_SHORT)
            {
                entityName = entityRow.ApplicationName;
                entityID = entityRow.ApplicationID;
                entityNameForDisplay = entityName;
                entityTypeForDisplay = "Application";
                metricsEntityFolderPath = metricsEntityFolderTypePath;
            }
            else if (entityType == TIERS_TYPE_SHORT)
            {
                entityName = entityRow.TierName;
                entityID = entityRow.TierID;
                entityNameForDisplay = entityName;
                entityTypeForDisplay = "Tier";
                metricsEntityFolderPath = Path.Combine(
                    metricsEntityFolderTypePath,
                    getShortenedEntityNameForFileSystem(entityRow.TierName, entityRow.TierID));
            }
            else if (entityType == NODES_TYPE_SHORT)
            {
                entityName = entityRow.NodeName;
                entityID = entityRow.NodeID;
                entityNameForDisplay = String.Format(@"{0}\{1}", entityRow.TierName, entityName);
                entityTypeForDisplay = "Node";
                metricsEntityFolderPath = Path.Combine(
                    metricsEntityFolderTypePath,
                    getShortenedEntityNameForFileSystem(entityRow.TierName, entityRow.TierID),
                    getShortenedEntityNameForFileSystem(entityRow.NodeName, entityRow.NodeID));
            }
            else if (entityType == BACKENDS_TYPE_SHORT)
            {
                entityName = ((EntityBackend)entityRow).BackendName;
                entityID = ((EntityBackend)entityRow).BackendID;
                entityNameForDisplay = entityName;
                entityTypeForDisplay = "Backend";
                metricsEntityFolderPath = Path.Combine(
                    metricsEntityFolderTypePath,
                    getShortenedEntityNameForFileSystem(((EntityBackend)entityRow).BackendName, ((EntityBackend)entityRow).BackendID));
            }
            else if (entityType == BUSINESS_TRANSACTIONS_TYPE_SHORT)
            {
                entityName = ((EntityBusinessTransaction)entityRow).BTName;
                entityID = ((EntityBusinessTransaction)entityRow).BTID;
                entityNameForDisplay = String.Format(@"{0}\{1}", entityRow.TierName, entityName);
                entityTypeForDisplay = "Business Transaction";
                metricsEntityFolderPath = Path.Combine(
                    metricsEntityFolderTypePath,
                    getShortenedEntityNameForFileSystem(entityRow.TierName, entityRow.TierID),
                    getShortenedEntityNameForFileSystem(((EntityBusinessTransaction)entityRow).BTName, ((EntityBusinessTransaction)entityRow).BTID));
            }
            else if (entityType == SERVICE_ENDPOINTS_TYPE_SHORT)
            {
                entityName = ((EntityServiceEndpoint)entityRow).SEPName;
                entityID = ((EntityServiceEndpoint)entityRow).SEPID;
                entityNameForDisplay = String.Format(@"{0}\{1}", entityRow.TierName, entityName);
                entityTypeForDisplay = "Service Endpoint";
                metricsEntityFolderPath = Path.Combine(
                    metricsEntityFolderTypePath,
                    getShortenedEntityNameForFileSystem(entityRow.TierName, entityRow.TierID),
                    getShortenedEntityNameForFileSystem(((EntityServiceEndpoint)entityRow).SEPName, ((EntityServiceEndpoint)entityRow).SEPID));
            }
            else if (entityType == ERRORS_TYPE_SHORT)
            {
                entityName = ((EntityError)entityRow).ErrorName;
                entityID = ((EntityError)entityRow).ErrorID;
                entityNameForDisplay = String.Format(@"{0}\{1}", entityRow.TierName, entityName);
                entityTypeForDisplay = "Error";
                metricsEntityFolderPath = Path.Combine(
                    metricsEntityFolderTypePath,
                    getShortenedEntityNameForFileSystem(entityRow.TierName, entityRow.TierID),
                    getShortenedEntityNameForFileSystem(((EntityError)entityRow).ErrorName, ((EntityError)entityRow).ErrorID));
            }

            #endregion

            logger.Info("Creating Entity Metrics Report for Metrics in {0}", metricsEntityFolderPath);

            #region Parameter sheet

            ExcelWorksheet sheetParameters = excelEntityDetail.Workbook.Worksheets[REPORT_SHEET_PARAMETERS];

            int l = sheetParameters.Dimension.Rows + 2;
            sheetParameters.Cells[l, 1].Value = "Entity Type";
            sheetParameters.Cells[l, 2].Value = entityTypeForDisplay;
            l++;
            sheetParameters.Cells[l, 1].Value = "Entity Name";
            sheetParameters.Cells[l, 2].Value = entityNameForDisplay;

            #endregion

            #region Entity Metrics Summary sheet

            ExcelWorksheet sheetMetrics = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_SUMMARY);
            sheetMetrics.Cells[1, 1].Value = "Table of Contents";
            sheetMetrics.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
            sheetMetrics.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

            if (jobConfiguration.Input.Metrics == true)
            {
                #region Full ranges

                // Full range table
                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                entityFullRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_FULLRANGE_FILE_NAME);
                range = readCSVFileIntoExcelRange(entityFullRangeReportFilePath, 0, sheetMetrics, fromRow, 1);
                if (range != null)
                {
                    table = sheetMetrics.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_ENTITY_FULL);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(entityType, sheetMetrics, table);

                    fromRow = fromRow + range.Rows + 2;
                }

                #endregion
                
                #region Hourly range

                // Hourly table
                sheetMetrics.Cells[fromRow - 1, 1].Value = "Hourly";

                entityHourlyRangeReportFilePath = Path.Combine(metricsEntityFolderPath, CONVERT_ENTITY_METRICS_HOURLY_FILE_NAME);
                range = readCSVFileIntoExcelRange(entityHourlyRangeReportFilePath, 0, sheetMetrics, fromRow, 1);
                if (range != null)
                {
                    table = sheetMetrics.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_ENTITY_HOURLY);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    adjustColumnsOfEntityRowTableInMetricReport(entityType, sheetMetrics, table);

                    // Remove DetailLink column because this document would be pointing to relative location from the overall metric reports, and the link would be invalid
                    sheetMetrics.DeleteColumn(table.Columns["DetailLink"].Position + 1);
                }

                #endregion
            }

            #endregion

            #region Entity Metric Detail sheet

            ExcelWorksheet sheetMetricsDetail = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_METRICS);
            sheetMetricsDetail.Cells[1, 1].Value = "Table of Contents";
            sheetMetricsDetail.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
            sheetMetricsDetail.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

            #region ART Table

            // ART table
            fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
            int fromColumnMetricSummary = 1;

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_ART_SHORTNAME);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(entityMetricSummaryReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricSummaryReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_DESCRIPTION, entityType, METRIC_ART_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["PropertyName"].Position + fromColumnMetricSummary).AutoFit();
                    sheetMetricsDetail.Column(table.Columns["PropertyValue"].Position + fromColumnMetricSummary).Width = 20;

                    fromRow = fromRow + range.Rows + 2;
                }
            }

            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(entityMetricValuesReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricValuesReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_VALUES, entityType, METRIC_ART_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["EventTimeStamp"].Position + fromColumnMetricSummary).AutoFit();

                    tableValuesART = table;
                }
            }

            #endregion

            #region CPM table

            fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
            fromColumnMetricSummary = sheetMetricsDetail.Dimension.Columns + 2;

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_CPM_SHORTNAME);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(entityMetricSummaryReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricSummaryReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_DESCRIPTION, entityType, METRIC_CPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["PropertyName"].Position + fromColumnMetricSummary).AutoFit();
                    sheetMetricsDetail.Column(table.Columns["PropertyValue"].Position + fromColumnMetricSummary).Width = 20;

                    fromRow = fromRow + range.Rows + 2;
                }
            }

            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(entityMetricValuesReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricValuesReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_VALUES, entityType, METRIC_CPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["EventTimeStamp"].Position + fromColumnMetricSummary).AutoFit();

                    tableValuesCPM = table;
                }
            }

            #endregion

            #region EPM table

            fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
            fromColumnMetricSummary = sheetMetricsDetail.Dimension.Columns + 2;

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EPM_SHORTNAME);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(entityMetricSummaryReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricSummaryReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_DESCRIPTION, entityType, METRIC_EPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["PropertyName"].Position + fromColumnMetricSummary).AutoFit();
                    sheetMetricsDetail.Column(table.Columns["PropertyValue"].Position + fromColumnMetricSummary).Width = 20;

                    fromRow = fromRow + range.Rows + 2;
                }
            }

            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(entityMetricValuesReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricValuesReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_VALUES, entityType, METRIC_EPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["EventTimeStamp"].Position + fromColumnMetricSummary).AutoFit();

                    tableValuesEPM = table;
                }
            }

            #endregion

            #region EXCPM table

            fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
            fromColumnMetricSummary = sheetMetricsDetail.Dimension.Columns + 2;

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EXCPM_SHORTNAME);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(entityMetricSummaryReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricSummaryReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_DESCRIPTION, entityType, METRIC_EXCPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["PropertyName"].Position + fromColumnMetricSummary).AutoFit();
                    sheetMetrics.Column(table.Columns["PropertyValue"].Position + fromColumnMetricSummary).Width = 20;

                    fromRow = fromRow + range.Rows + 2;
                }
            }

            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(entityMetricValuesReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricValuesReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_VALUES, entityType, METRIC_EXCPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["EventTimeStamp"].Position + fromColumnMetricSummary).AutoFit();

                    tableValuesEXCPM = table;

                    fromRow = fromRow + range.Rows + 2;
                }
            }

            #endregion

            #region HTTPEPM table

            fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
            fromColumnMetricSummary = sheetMetricsDetail.Dimension.Columns + 2;

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_HTTPEPM_SHORTNAME);
            entityMetricSummaryReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_SUMMARY_FILE_NAME);
            if (File.Exists(entityMetricSummaryReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricSummaryReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_DESCRIPTION, entityType, METRIC_HTTPEPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["PropertyName"].Position + fromColumnMetricSummary).AutoFit();
                    sheetMetricsDetail.Column(table.Columns["PropertyValue"].Position + fromColumnMetricSummary).Width = 20;

                    fromRow = fromRow + range.Rows + 2;
                }
            }

            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            if (File.Exists(entityMetricValuesReportFilePath) == true)
            {
                range = readCSVFileIntoExcelRange(entityMetricValuesReportFilePath, 0, sheetMetricsDetail, fromRow, fromColumnMetricSummary);
                if (range != null)
                {
                    if (range.Rows == 1)
                    {
                        // If there was no data in the table, adjust the range to have at least one blank line, otherwise Excel thinks table is corrupt
                        range = sheetMetricsDetail.Cells[range.Start.Row, range.Start.Column, range.End.Row + 1, range.End.Column];
                    }
                    table = sheetMetricsDetail.Tables.Add(range, String.Format(REPORT_ENTITY_DETAILS_METRIC_TABLE_METRIC_VALUES, entityType, METRIC_HTTPEPM_SHORTNAME));
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetMetricsDetail.Column(table.Columns["EventTimeStamp"].Position + fromColumnMetricSummary).AutoFit();

                    tableValuesHTTPEPM = table;
                }
            }

            #endregion

            #endregion

            #region Activity grid sheet

            ExcelWorksheet sheetActivityGrid = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_ACTIVITYGRID);
            sheetActivityGrid.Cells[1, 1].Value = "Table of Contents";
            sheetActivityGrid.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
            sheetActivityGrid.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

            fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
            string activityGridReportFileName = Path.Combine(metricsEntityFolderPath, CONVERT_ACTIVITY_GRID_FILE_NAME);
            range = readCSVFileIntoExcelRange(activityGridReportFileName, 0, sheetActivityGrid, fromRow, 1);
            if (range != null && range.Rows > 1)
            {
                table = sheetActivityGrid.Tables.Add(range, REPORT_ENTITY_DETAILS_ACTIVITY_GRID);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;

                sheetActivityGrid.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheetActivityGrid.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheetActivityGrid.Column(table.Columns["CallType"].Position + 1).Width = 10;
                sheetActivityGrid.Column(table.Columns["FromName"].Position + 1).Width = 35;
                sheetActivityGrid.Column(table.Columns["ToName"].Position + 1).Width = 35;
                sheetActivityGrid.Column(table.Columns["From"].Position + 1).AutoFit();
                sheetActivityGrid.Column(table.Columns["To"].Position + 1).AutoFit();
                sheetActivityGrid.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheetActivityGrid.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                //sheetActivityGrid.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheetActivityGrid.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheetActivityGrid.Column(table.Columns["FromLink"].Position + 1).AutoFit();
                //sheetActivityGrid.Column(table.Columns["ToLink"].Position + 1).AutoFit();
                //sheetActivityGrid.Column(table.Columns["MetricLink"].Position + 1).AutoFit();
            }

            #endregion

            #region Events and Health Rule violations

            #region Filter events by type of entity output

            // Filter events if necessary
            string eventsFilePath = Path.Combine(eventsFolderPath, CONVERT_EVENTS_FILE_NAME);
            string eventsFilePathFiltered = String.Empty;
            string healthRuleViolationEventsFilePath = Path.Combine(eventsFolderPath, CONVERT_HEALTH_RULE_EVENTS_FILE_NAME);
            string healthRuleViolationEventsFilePathFiltered = String.Empty;
            List<Event> eventsFilteredList = null;
            List<HealthRuleViolationEvent> healthRuleViolationFilteredList = null;

            switch (entityType)
            {
                case APPLICATION_TYPE_SHORT:
                    // The Application report has all events
                    eventsFilteredList = FileIOHelper.readListFromCSVFile<Event>(eventsFilePath, new EventReportMap());
                    break;

                case TIERS_TYPE_SHORT:
                    // Filter
                    eventsFilteredList = FileIOHelper.readListFromCSVFile<Event>(eventsFilePath, new EventReportMap());
                    if (eventsFilteredList != null)
                    {
                        eventsFilteredList = eventsFilteredList.Where(e => e.TierID == entityRow.TierID).ToList();
                        if (eventsFilteredList.Count > 0)
                        {
                            eventsFilePathFiltered = Path.Combine(eventsFolderPath, String.Format(CONVERT_EVENTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                            FileIOHelper.writeListToCSVFile<Event>(eventsFilteredList, new EventReportMap(), eventsFilePathFiltered);
                            eventsFilePath = eventsFilePathFiltered;
                        }
                        else
                        {
                            eventsFilePath = String.Empty;
                        }
                    }

                    healthRuleViolationFilteredList = FileIOHelper.readListFromCSVFile<HealthRuleViolationEvent>(healthRuleViolationEventsFilePath, new HealthRuleViolationEventReportMap());
                    if (healthRuleViolationFilteredList != null)
                    {
                        healthRuleViolationFilteredList = healthRuleViolationFilteredList.Where(e => e.EntityType == entityTypeStringMapping[ENTITY_TYPE_TIER] && e.EntityID == entityRow.TierID).ToList();
                        if (healthRuleViolationFilteredList.Count > 0)
                        {
                            healthRuleViolationEventsFilePathFiltered = Path.Combine(eventsFolderPath, String.Format(CONVERT_HEALTH_RULE_EVENTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                            FileIOHelper.writeListToCSVFile<HealthRuleViolationEvent>(healthRuleViolationFilteredList, new HealthRuleViolationEventReportMap(), healthRuleViolationEventsFilePathFiltered);
                            healthRuleViolationEventsFilePath = healthRuleViolationEventsFilePathFiltered;
                        }
                        else
                        {
                            healthRuleViolationEventsFilePath = String.Empty;
                        }
                    }

                    break;

                case NODES_TYPE_SHORT:
                    // Filter
                    eventsFilteredList = FileIOHelper.readListFromCSVFile<Event>(eventsFilePath, new EventReportMap());
                    if (eventsFilteredList != null)
                    {
                        eventsFilteredList = eventsFilteredList.Where(e => e.NodeID == entityRow.NodeID).ToList();
                        if (eventsFilteredList.Count > 0)
                        {
                            eventsFilePathFiltered = Path.Combine(eventsFolderPath, String.Format(CONVERT_EVENTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                            FileIOHelper.writeListToCSVFile<Event>(eventsFilteredList, new EventReportMap(), eventsFilePathFiltered);
                            eventsFilePath = eventsFilePathFiltered;
                        }
                        else
                        {
                            eventsFilePath = String.Empty;
                        }
                    }

                    healthRuleViolationFilteredList = FileIOHelper.readListFromCSVFile<HealthRuleViolationEvent>(healthRuleViolationEventsFilePath, new HealthRuleViolationEventReportMap());
                    if (healthRuleViolationFilteredList != null)
                    {
                        healthRuleViolationFilteredList = healthRuleViolationFilteredList.Where(e => e.EntityType == entityTypeStringMapping[ENTITY_TYPE_NODE] && e.EntityID == entityRow.NodeID).ToList();
                        if (healthRuleViolationFilteredList.Count > 0)
                        {
                            healthRuleViolationEventsFilePathFiltered = Path.Combine(eventsFolderPath, String.Format(CONVERT_HEALTH_RULE_EVENTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                            FileIOHelper.writeListToCSVFile<HealthRuleViolationEvent>(healthRuleViolationFilteredList, new HealthRuleViolationEventReportMap(), healthRuleViolationEventsFilePathFiltered);
                            healthRuleViolationEventsFilePath = healthRuleViolationEventsFilePathFiltered;
                        }
                        else
                        {
                            healthRuleViolationEventsFilePath = String.Empty;
                        }
                    }

                    break;

                case BUSINESS_TRANSACTIONS_TYPE_SHORT:
                    // Filter
                    eventsFilteredList = FileIOHelper.readListFromCSVFile<Event>(eventsFilePath, new EventReportMap());
                    if (eventsFilteredList != null)
                    {
                        eventsFilteredList = eventsFilteredList.Where(e => e.BTID == ((EntityBusinessTransaction)entityRow).BTID).ToList();
                        if (eventsFilteredList.Count > 0)
                        {
                            eventsFilePathFiltered = Path.Combine(eventsFolderPath, String.Format(CONVERT_EVENTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                            FileIOHelper.writeListToCSVFile<Event>(eventsFilteredList, new EventReportMap(), eventsFilePathFiltered);
                            eventsFilePath = eventsFilePathFiltered;
                        }
                        else
                        {
                            eventsFilePath = String.Empty;
                        }
                    }

                    healthRuleViolationFilteredList = FileIOHelper.readListFromCSVFile<HealthRuleViolationEvent>(healthRuleViolationEventsFilePath, new HealthRuleViolationEventReportMap());
                    if (healthRuleViolationFilteredList != null)
                    {
                        healthRuleViolationFilteredList = healthRuleViolationFilteredList.Where(e => e.EntityType == entityTypeStringMapping[ENTITY_TYPE_BUSINESS_TRANSACTION] && e.EntityID == ((EntityBusinessTransaction)entityRow).BTID).ToList();
                        if (healthRuleViolationFilteredList.Count > 0)
                        {
                            healthRuleViolationEventsFilePathFiltered = Path.Combine(eventsFolderPath, String.Format(CONVERT_HEALTH_RULE_EVENTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                            FileIOHelper.writeListToCSVFile<HealthRuleViolationEvent>(healthRuleViolationFilteredList, new HealthRuleViolationEventReportMap(), healthRuleViolationEventsFilePathFiltered);
                            healthRuleViolationEventsFilePath = healthRuleViolationEventsFilePathFiltered;
                        }
                        else
                        {
                            healthRuleViolationEventsFilePath = String.Empty;
                        }
                    }

                    break;

                default:
                    // Display nothing
                    eventsFilePath = String.Empty;
                    healthRuleViolationEventsFilePath = String.Empty;
                    break;
            }

            #endregion

            #region Events sheet

            if (eventsFilePath != String.Empty)
            {
                // Detail
                ExcelWorksheet sheetEvents = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_EVENTS);
                sheetEvents.Cells[1, 1].Value = "Table of Contents";
                sheetEvents.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetEvents.Cells[2, 1].Value = "See Pivot";
                sheetEvents.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS_PIVOT);
                sheetEvents.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                // Pivot
                ExcelWorksheet sheetEventsPivot = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_EVENTS_PIVOT);
                sheetEventsPivot.Cells[1, 1].Value = "Table of Contents";
                sheetEventsPivot.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetEventsPivot.Cells[2, 1].Value = "See Table";
                sheetEventsPivot.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS);
                sheetEventsPivot.View.FreezePanes(REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT + 2, 9);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(eventsFilePath, 0, sheetEvents, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetEvents.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_EVENTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetEvents.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["EventID"].Position + 1).AutoFit();
                    sheetEvents.Column(table.Columns["Occured"].Position + 1).AutoFit();
                    sheetEvents.Column(table.Columns["OccuredUtc"].Position + 1).AutoFit();
                    sheetEvents.Column(table.Columns["Summary"].Position + 1).Width = 35;
                    sheetEvents.Column(table.Columns["Type"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["SubType"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["TriggeredEntityType"].Position + 1).Width = 20;
                    sheetEvents.Column(table.Columns["TriggeredEntityName"].Position + 1).Width = 20;
                    //sheetEvents.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                    //sheetEvents.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                    //sheetEvents.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                    //sheetEvents.Column(table.Columns["NodeLink"].Position + 1).AutoFit();
                    //sheetEvents.Column(table.Columns["BTLink"].Position + 1).AutoFit();
                    //sheetEvents.Column(table.Columns["EventLink"].Position + 1).AutoFit();

                    tableEvents = table;

                    ExcelPivotTable pivot = sheetEventsPivot.PivotTables.Add(sheetEventsPivot.Cells[REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_ENTITY_DETAILS_PIVOT_EVENTS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Type"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["SubType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Severity"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["NodeName"]);
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["EventID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }
            }

            #endregion

            #region HR Violations sheet

            if (healthRuleViolationEventsFilePath != String.Empty)
            { 
                // Details
                ExcelWorksheet sheetHRViolations = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_EVENTS_HR);
                sheetHRViolations.Cells[1, 1].Value = "Table of Contents";
                sheetHRViolations.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetHRViolations.Cells[2, 1].Value = "See Pivot";
                sheetHRViolations.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS_HR_PIVOT);
                sheetHRViolations.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                // Pivot
                ExcelWorksheet sheetHRViolationsPivot = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_EVENTS_HR_PIVOT);
                sheetHRViolationsPivot.Cells[1, 1].Value = "Table of Contents";
                sheetHRViolationsPivot.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetHRViolationsPivot.Cells[2, 1].Value = "See Table";
                sheetHRViolationsPivot.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS_HR);
                sheetHRViolationsPivot.View.FreezePanes(REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT + 2, 5);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(healthRuleViolationEventsFilePath, 0, sheetHRViolations, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetHRViolations.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_HEALTH_RULE_VIOLATION_EVENTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetHRViolations.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetHRViolations.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetHRViolations.Column(table.Columns["EventID"].Position + 1).AutoFit();
                    sheetHRViolations.Column(table.Columns["From"].Position + 1).AutoFit();
                    sheetHRViolations.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                    sheetHRViolations.Column(table.Columns["To"].Position + 1).AutoFit();
                    sheetHRViolations.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                    sheetHRViolations.Column(table.Columns["HealthRuleName"].Position + 1).Width = 20;
                    sheetHRViolations.Column(table.Columns["EntityName"].Position + 1).Width = 20;
                    //sheetHRViolations.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                    //sheetHRViolations.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                    //sheetHRViolations.Column(table.Columns["HealthRuleLink"].Position + 1).AutoFit();
                    //sheetHRViolations.Column(table.Columns["EntityLink"].Position + 1).AutoFit();
                    //sheetHRViolations.Column(table.Columns["EventLink"].Position + 1).AutoFit();

                    ExcelPivotTable pivot = sheetHRViolationsPivot.PivotTables.Add(sheetHRViolationsPivot.Cells[REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_ENTITY_DETAILS_PIVOT_HEALTH_RULE_VIOLATION_EVENTS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Severity"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Status"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["EntityType"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    fieldC = pivot.ColumnFields.Add(pivot.Fields["EntityName"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    fieldC = pivot.ColumnFields.Add(pivot.Fields["HealthRuleName"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["EventID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }
            }

            #endregion

            #region Clear out temporary files for Events and Health Rule Violations

            if (eventsFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(eventsFilePathFiltered);
            }
            if (healthRuleViolationEventsFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(healthRuleViolationEventsFilePathFiltered);
            }

            #endregion

            #endregion

            #region Snapshots, Segments, Exit Calls, Service Endpoint Calls, Business Data

            #region Filter events by type of entity output

            string snapshotsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_FILE_NAME);
            string snapshotsFilePathFiltered = String.Empty;
            string segmentsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_FILE_NAME);
            string segmentsFilePathFiltered = String.Empty;
            string exitCallsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_EXIT_CALLS_FILE_NAME);
            string exitCallsFilePathFiltered = String.Empty;
            string serviceEndpointCallsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_SERVICE_ENDPOINTS_CALLS_FILE_NAME);
            string serviceEndpointCallsFilePathFiltered = String.Empty;
            string detectedErrorsFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_DETECTED_ERRORS_FILE_NAME);
            string detectedErrorsFilePathFiltered = String.Empty;
            string businessDataFilePath = Path.Combine(snapshotsFolderPath, CONVERT_SNAPSHOTS_SEGMENTS_BUSINESS_DATA_FILE_NAME);
            string businessDataFilePathFiltered = String.Empty;

            List<Snapshot> snapshotsFilteredList = null;
            List<Segment> segmentsFilteredList = null;
            List<ExitCall> exitCallsFilteredList = null;
            List<ServiceEndpointCall> serviceEndpointCallsFilteredList = null;
            List<DetectedError> detectedErrorsFilteredList = null;
            List<BusinessData> businessDataFilteredList = null;

            List<Snapshot> snapshotsAllList = null;
            List<Segment> segmentsAllList = null;
            List<ExitCall> exitCallsAllList = null;
            List<ServiceEndpointCall> serviceEndpointCallsAllList = null;
            List<DetectedError> detectedErrorsAllList = null;
            List<BusinessData> businessDataAllList = null;

            switch (entityType)
            {
                case APPLICATION_TYPE_SHORT:
                    // The Application report has all snapshots, segments, call exits etc.
                    snapshotsFilteredList = FileIOHelper.readListFromCSVFile<Snapshot>(snapshotsFilePath, new SnapshotReportMap());

                    break;

                case TIERS_TYPE_SHORT:
                    // Filter snapshots starting at this Tier
                    snapshotsAllList = FileIOHelper.readListFromCSVFile<Snapshot>(snapshotsFilePath, new SnapshotReportMap());
                    List<Snapshot> snapshotsStartingAtThisEntity = null;
                    if (snapshotsAllList != null)
                    {
                        snapshotsStartingAtThisEntity = snapshotsAllList.Where(s => s.TierID == entityRow.TierID).ToList();
                    }

                    // Filter snapshots that start elsewhere, but include this tier
                    List<Snapshot> snapshotsCrossingThisEntity = new List<Snapshot>();
                    segmentsFilteredList = new List<Segment>();
                    segmentsAllList = FileIOHelper.readListFromCSVFile<Segment>(segmentsFilePath, new SegmentReportMap());
                    if (segmentsAllList != null)
                    {
                        var uniqueSnapshotIDs = segmentsAllList.Where(s => s.TierID == entityRow.TierID).ToList().Select(e => e.RequestID).Distinct();
                        foreach (string requestID in uniqueSnapshotIDs)
                        {
                            Snapshot snapshotForThisRequest = snapshotsAllList.Find(s => s.RequestID == requestID);
                            if (snapshotForThisRequest != null)
                            {
                                snapshotsCrossingThisEntity.Add(snapshotForThisRequest);
                            }

                            List<Segment> segmentRowsForThisRequest = segmentsAllList.Where(s => s.RequestID == requestID).ToList();
                            segmentsFilteredList.AddRange(segmentRowsForThisRequest);
                        }
                    }

                    // Combine both and make them unique
                    snapshotsFilteredList = new List<Snapshot>();
                    if (snapshotsStartingAtThisEntity != null) { snapshotsFilteredList.AddRange(snapshotsStartingAtThisEntity); }
                    if (snapshotsCrossingThisEntity != null) { snapshotsFilteredList.AddRange(snapshotsCrossingThisEntity); }
                    snapshotsFilteredList = snapshotsFilteredList.Distinct().ToList();

                    // Filter exit calls to the list of snapshots
                    exitCallsFilteredList = new List<ExitCall>();
                    exitCallsAllList = FileIOHelper.readListFromCSVFile<ExitCall>(exitCallsFilePath, new ExitCallReportMap());
                    if (exitCallsAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            exitCallsFilteredList.AddRange(exitCallsAllList.Where(e => e.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    // Filter Service Endpoint Calls to the list of snapshots
                    serviceEndpointCallsFilteredList = new List<ServiceEndpointCall>();
                    serviceEndpointCallsAllList = FileIOHelper.readListFromCSVFile<ServiceEndpointCall>(serviceEndpointCallsFilePath, new ServiceEndpointCallReportMap());
                    if (serviceEndpointCallsAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            serviceEndpointCallsFilteredList.AddRange(serviceEndpointCallsAllList.Where(s => s.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    // Filter Detected Errors to the list of snapshots
                    detectedErrorsFilteredList = new List<DetectedError>();
                    detectedErrorsAllList = FileIOHelper.readListFromCSVFile<DetectedError>(detectedErrorsFilePath, new DetectedErrorReportMap());
                    if (detectedErrorsAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            detectedErrorsFilteredList.AddRange(detectedErrorsAllList.Where(s => s.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    // Filter Business Data to the list of snapshots
                    businessDataFilteredList = new List<BusinessData>();
                    businessDataAllList = FileIOHelper.readListFromCSVFile<BusinessData>(businessDataFilePath, new BusinessDataReportMap());
                    if (businessDataAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            businessDataFilteredList.AddRange(businessDataAllList.Where(s => s.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    break;

                case NODES_TYPE_SHORT:
                    // Filter snapshots starting at this Tier and Node
                    snapshotsAllList = FileIOHelper.readListFromCSVFile<Snapshot>(snapshotsFilePath, new SnapshotReportMap());
                    snapshotsStartingAtThisEntity = null;
                    if (snapshotsAllList != null)
                    {
                        snapshotsStartingAtThisEntity = snapshotsAllList.Where(s => s.TierID == entityRow.TierID && s.NodeID == entityRow.NodeID).ToList();
                    }

                    // Filter snapshots starting elsewhere, but including this Tier and Node
                    snapshotsCrossingThisEntity = new List<Snapshot>();
                    segmentsFilteredList = new List<Segment>();
                    segmentsAllList = FileIOHelper.readListFromCSVFile<Segment>(segmentsFilePath, new SegmentReportMap());
                    if (segmentsAllList != null)
                    {
                        var uniqueSnapshotIDs = segmentsAllList.Where(s => s.TierID == entityRow.TierID && s.NodeID == entityRow.NodeID).ToList().Select(e => e.RequestID).Distinct();
                        foreach (string requestID in uniqueSnapshotIDs)
                        {
                            Snapshot snapshotForThisRequest = snapshotsAllList.Find(s => s.RequestID == requestID);
                            if (snapshotForThisRequest != null)
                            {
                                snapshotsCrossingThisEntity.Add(snapshotForThisRequest);
                            }

                            List<Segment> segmentsForThisRequestList = segmentsAllList.Where(s => s.RequestID == requestID).ToList();
                            segmentsFilteredList.AddRange(segmentsForThisRequestList);
                        }
                    }

                    // Combine both and make them unique
                    snapshotsFilteredList = new List<Snapshot>();
                    if (snapshotsStartingAtThisEntity != null) { snapshotsFilteredList.AddRange(snapshotsStartingAtThisEntity); }
                    if (snapshotsCrossingThisEntity != null) { snapshotsFilteredList.AddRange(snapshotsCrossingThisEntity); }
                    snapshotsFilteredList = snapshotsFilteredList.Distinct().ToList();

                    // Filter exit calls to the list of snapshots
                    exitCallsFilteredList = new List<ExitCall>();
                    exitCallsAllList = FileIOHelper.readListFromCSVFile<ExitCall>(exitCallsFilePath, new ExitCallReportMap());
                    if (exitCallsAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            exitCallsFilteredList.AddRange(exitCallsAllList.Where(e => e.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    // Filter Service Endpoint Calls to the list of snapshots
                    serviceEndpointCallsFilteredList = new List<ServiceEndpointCall>();
                    serviceEndpointCallsAllList = FileIOHelper.readListFromCSVFile<ServiceEndpointCall>(serviceEndpointCallsFilePath, new ServiceEndpointCallReportMap());
                    if (serviceEndpointCallsAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            serviceEndpointCallsFilteredList.AddRange(serviceEndpointCallsAllList.Where(s => s.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    // Filter Detected Errors to the list of snapshots
                    detectedErrorsFilteredList = new List<DetectedError>();
                    detectedErrorsAllList = FileIOHelper.readListFromCSVFile<DetectedError>(detectedErrorsFilePath, new DetectedErrorReportMap());
                    if (detectedErrorsAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            detectedErrorsFilteredList.AddRange(detectedErrorsAllList.Where(s => s.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    // Filter Business Data to the list of snapshots
                    businessDataFilteredList = new List<BusinessData>();
                    businessDataAllList = FileIOHelper.readListFromCSVFile<BusinessData>(businessDataFilePath, new BusinessDataReportMap());
                    if (businessDataAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            businessDataFilteredList.AddRange(businessDataAllList.Where(s => s.RequestID == snapshot.RequestID).ToList());
                        }
                    }

                    break;

                case BACKENDS_TYPE_SHORT:
                    // Filter snapshots calling this Backend
                    snapshotsAllList = FileIOHelper.readListFromCSVFile<Snapshot>(snapshotsFilePath, new SnapshotReportMap());
                    segmentsAllList = FileIOHelper.readListFromCSVFile<Segment>(segmentsFilePath, new SegmentReportMap());
                    serviceEndpointCallsAllList = FileIOHelper.readListFromCSVFile<ServiceEndpointCall>(serviceEndpointCallsFilePath, new ServiceEndpointCallReportMap());
                    detectedErrorsAllList = FileIOHelper.readListFromCSVFile<DetectedError>(detectedErrorsFilePath, new DetectedErrorReportMap());
                    businessDataAllList = FileIOHelper.readListFromCSVFile<BusinessData>(businessDataFilePath, new BusinessDataReportMap());

                    snapshotsFilteredList = new List<Snapshot>();
                    segmentsFilteredList = new List<Segment>();
                    exitCallsFilteredList = new List<ExitCall>();
                    serviceEndpointCallsFilteredList = new List<ServiceEndpointCall>();
                    detectedErrorsFilteredList = new List<DetectedError>();
                    businessDataFilteredList = new List<BusinessData>();

                    // Filter the backends
                    exitCallsAllList = FileIOHelper.readListFromCSVFile<ExitCall>(exitCallsFilePath, new ExitCallReportMap());
                    if (exitCallsAllList != null)
                    {
                        var uniqueSnapshotIDs = exitCallsAllList.Where(e => e.ToEntityID == ((EntityBackend)entityRow).BackendID).ToList().Select(e => e.RequestID).Distinct();
                        foreach (string requestID in uniqueSnapshotIDs)
                        {
                            if (snapshotsAllList != null)
                            {
                                snapshotsFilteredList.AddRange(snapshotsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (segmentsAllList != null)
                            {
                                segmentsFilteredList.AddRange(segmentsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            exitCallsFilteredList.AddRange(exitCallsAllList.Where(e => e.RequestID == requestID).ToList());
                            if (serviceEndpointCallsAllList != null)
                            {
                                serviceEndpointCallsFilteredList.AddRange(serviceEndpointCallsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (detectedErrorsAllList != null)
                            {
                                detectedErrorsFilteredList.AddRange(detectedErrorsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (businessDataAllList != null)
                            {
                                businessDataFilteredList.AddRange(businessDataAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                        }
                    }

                    break;

                case BUSINESS_TRANSACTIONS_TYPE_SHORT:
                    // Filter Snapshots by BTs 
                    snapshotsFilteredList = new List<Snapshot>();
                    segmentsFilteredList = new List<Segment>();
                    exitCallsFilteredList = new List<ExitCall>();
                    serviceEndpointCallsFilteredList = new List<ServiceEndpointCall>();

                    snapshotsAllList = FileIOHelper.readListFromCSVFile<Snapshot>(snapshotsFilePath, new SnapshotReportMap());
                    if (snapshotsAllList != null)
                    {
                        snapshotsFilteredList = snapshotsAllList.Where(s => s.BTID == ((EntityBusinessTransaction)entityRow).BTID).ToList();
                    }

                    // Filter Segments by BTs 
                    segmentsAllList = FileIOHelper.readListFromCSVFile<Segment>(segmentsFilePath, new SegmentReportMap());
                    if (segmentsAllList != null)
                    {
                        segmentsFilteredList = segmentsAllList.Where(s => s.BTID == ((EntityBusinessTransaction)entityRow).BTID).ToList();
                    }

                    // Filter Exits by BTs 
                    exitCallsAllList = FileIOHelper.readListFromCSVFile<ExitCall>(exitCallsFilePath, new ExitCallReportMap());
                    if (exitCallsAllList != null)
                    {
                        exitCallsFilteredList = exitCallsAllList.Where(e => e.BTID == ((EntityBusinessTransaction)entityRow).BTID).ToList();
                    }

                    // Filter Service Endpoint Calls by BTs 
                    serviceEndpointCallsAllList = FileIOHelper.readListFromCSVFile<ServiceEndpointCall>(serviceEndpointCallsFilePath, new ServiceEndpointCallReportMap());
                    if (serviceEndpointCallsAllList != null)
                    {
                        serviceEndpointCallsFilteredList = serviceEndpointCallsAllList.Where(s => s.BTID == ((EntityBusinessTransaction)entityRow).BTID).ToList();
                    }

                    // Filter Detected Errors by BTs
                    detectedErrorsFilteredList = new List<DetectedError>();
                    detectedErrorsAllList = FileIOHelper.readListFromCSVFile<DetectedError>(detectedErrorsFilePath, new DetectedErrorReportMap());
                    if (detectedErrorsAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            detectedErrorsFilteredList.AddRange(detectedErrorsAllList.Where(s => s.BTID == ((EntityBusinessTransaction)entityRow).BTID).ToList());
                        }
                    }

                    // Filter Business Data to the list of snapshots
                    businessDataFilteredList = new List<BusinessData>();
                    businessDataAllList = FileIOHelper.readListFromCSVFile<BusinessData>(businessDataFilePath, new BusinessDataReportMap());
                    if (businessDataAllList != null)
                    {
                        foreach (Snapshot snapshot in snapshotsFilteredList)
                        {
                            businessDataFilteredList.AddRange(businessDataAllList.Where(s => s.BTID == ((EntityBusinessTransaction)entityRow).BTID).ToList());
                        }
                    }

                    break;

                case SERVICE_ENDPOINTS_TYPE_SHORT:
                    // Filter snapshots that call this SEP
                    snapshotsAllList = FileIOHelper.readListFromCSVFile<Snapshot>(snapshotsFilePath, new SnapshotReportMap());
                    segmentsAllList = FileIOHelper.readListFromCSVFile<Segment>(segmentsFilePath, new SegmentReportMap());
                    exitCallsAllList = FileIOHelper.readListFromCSVFile<ExitCall>(exitCallsFilePath, new ExitCallReportMap());
                    detectedErrorsAllList = FileIOHelper.readListFromCSVFile<DetectedError>(detectedErrorsFilePath, new DetectedErrorReportMap());

                    snapshotsFilteredList = new List<Snapshot>();
                    segmentsFilteredList = new List<Segment>();
                    exitCallsFilteredList = new List<ExitCall>();
                    serviceEndpointCallsFilteredList = new List<ServiceEndpointCall>();
                    detectedErrorsFilteredList = new List<DetectedError>();

                    serviceEndpointCallsAllList = FileIOHelper.readListFromCSVFile<ServiceEndpointCall>(serviceEndpointCallsFilePath, new ServiceEndpointCallReportMap());
                    if (serviceEndpointCallsAllList != null)
                    {
                        var uniqueSnapshotIDs = serviceEndpointCallsAllList.Where(s => s.SEPID == ((EntityServiceEndpoint)entityRow).SEPID).ToList().Select(e => e.RequestID).Distinct();
                        foreach (string requestID in uniqueSnapshotIDs)
                        {
                            if (snapshotsAllList != null)
                            {
                                snapshotsFilteredList.AddRange(snapshotsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (segmentsAllList != null)
                            {
                                segmentsFilteredList.AddRange(segmentsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (exitCallsAllList != null)
                            {
                                exitCallsFilteredList.AddRange(exitCallsAllList.Where(e => e.RequestID == requestID).ToList());
                            }
                            serviceEndpointCallsFilteredList.AddRange(serviceEndpointCallsAllList.Where(s => s.RequestID == requestID).ToList());
                            if (detectedErrorsAllList != null)
                            {
                                detectedErrorsFilteredList.AddRange(detectedErrorsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (businessDataAllList != null)
                            {
                                businessDataFilteredList.AddRange(businessDataAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                        }
                    }

                    break;

                case ERRORS_TYPE_SHORT:
                    // Filter snapshots that had this error
                    snapshotsAllList = FileIOHelper.readListFromCSVFile<Snapshot>(snapshotsFilePath, new SnapshotReportMap());
                    segmentsAllList = FileIOHelper.readListFromCSVFile<Segment>(segmentsFilePath, new SegmentReportMap());
                    serviceEndpointCallsAllList = FileIOHelper.readListFromCSVFile<ServiceEndpointCall>(serviceEndpointCallsFilePath, new ServiceEndpointCallReportMap());
                    exitCallsAllList = FileIOHelper.readListFromCSVFile<ExitCall>(exitCallsFilePath, new ExitCallReportMap());

                    snapshotsFilteredList = new List<Snapshot>();
                    segmentsFilteredList = new List<Segment>();
                    exitCallsFilteredList = new List<ExitCall>();
                    serviceEndpointCallsFilteredList = new List<ServiceEndpointCall>();
                    detectedErrorsFilteredList = new List<DetectedError>();

                    detectedErrorsAllList = FileIOHelper.readListFromCSVFile<DetectedError>(detectedErrorsFilePath, new DetectedErrorReportMap());
                    if (detectedErrorsAllList != null)
                    {
                        var uniqueSnapshotIDs = detectedErrorsAllList.Where(e => e.ErrorID == ((EntityError)entityRow).ErrorID).ToList().Select(e => e.RequestID).Distinct();
                        foreach (string requestID in uniqueSnapshotIDs)
                        {
                            if (snapshotsAllList != null)
                            {
                                snapshotsFilteredList.AddRange(snapshotsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (segmentsAllList != null)
                            {
                                segmentsFilteredList.AddRange(segmentsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            if (exitCallsAllList != null)
                            {
                                exitCallsFilteredList.AddRange(exitCallsAllList.Where(e => e.RequestID == requestID).ToList());
                            }
                            if (serviceEndpointCallsAllList != null)
                            {
                                serviceEndpointCallsFilteredList.AddRange(serviceEndpointCallsAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                            detectedErrorsFilteredList.AddRange(detectedErrorsAllList.Where(e => e.RequestID == requestID).ToList());
                            if (businessDataAllList != null)
                            {
                                businessDataFilteredList.AddRange(businessDataAllList.Where(s => s.RequestID == requestID).ToList());
                            }
                        }

                    }

                    break;

                default:
                    // Will never hit here, because all the values
                    // But do nothing anyway
                    snapshotsFilePath = String.Empty;
                    segmentsFilePath = String.Empty;
                    exitCallsFilePath = String.Empty;
                    serviceEndpointCallsFilePath = String.Empty;
                    detectedErrorsFilePath = String.Empty;
                    businessDataFilePath = String.Empty;

                    break;
            }

            // Save all the filtered things
            if (entityType != APPLICATION_TYPE_SHORT)
            {
                if (snapshotsFilteredList != null && snapshotsFilteredList.Count > 0)
                {
                    snapshotsFilePathFiltered = Path.Combine(snapshotsFolderPath, String.Format(CONVERT_SNAPSHOTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                    FileIOHelper.writeListToCSVFile<Snapshot>(snapshotsFilteredList, new SnapshotReportMap(), snapshotsFilePathFiltered);
                    snapshotsFilePath = snapshotsFilePathFiltered;
                }
                else
                {
                    snapshotsFilePath = String.Empty;
                }

                if (segmentsFilteredList != null && segmentsFilteredList.Count > 0)
                {
                    segmentsFilePathFiltered = Path.Combine(snapshotsFolderPath, String.Format(CONVERT_SNAPSHOTS_SEGMENTS_FILTERED_FILE_NAME, Guid.NewGuid()));
                    FileIOHelper.writeListToCSVFile<Segment>(segmentsFilteredList, new SegmentReportMap(), segmentsFilePathFiltered);
                    segmentsFilePath = segmentsFilePathFiltered;
                }
                else
                {
                    segmentsFilePath = String.Empty;
                }

                if (exitCallsFilteredList != null && exitCallsFilteredList.Count > 0)
                {
                    exitCallsFilePathFiltered = Path.Combine(snapshotsFolderPath, String.Format(CONVERT_SNAPSHOTS_SEGMENTS_EXIT_CALLS_FILTERED_FILE_NAME, Guid.NewGuid()));
                    FileIOHelper.writeListToCSVFile<ExitCall>(exitCallsFilteredList, new ExitCallReportMap(), exitCallsFilePathFiltered);
                    exitCallsFilePath = exitCallsFilePathFiltered;
                }
                else
                {
                    exitCallsFilePath = String.Empty;
                }

                if (serviceEndpointCallsFilteredList != null && serviceEndpointCallsFilteredList.Count > 0)
                {
                    serviceEndpointCallsFilePathFiltered = Path.Combine(snapshotsFolderPath, String.Format(CONVERT_SNAPSHOTS_SEGMENTS_SERVICE_ENDPOINT_CALLS_FILTERED_FILE_NAME, Guid.NewGuid()));
                    FileIOHelper.writeListToCSVFile<ServiceEndpointCall>(serviceEndpointCallsFilteredList, new ServiceEndpointCallReportMap(), serviceEndpointCallsFilePathFiltered);
                    serviceEndpointCallsFilePath = serviceEndpointCallsFilePathFiltered;
                }
                else
                {
                    serviceEndpointCallsFilePath = String.Empty;
                }

                if (detectedErrorsFilteredList != null && detectedErrorsFilteredList.Count > 0)
                {
                    detectedErrorsFilePathFiltered = Path.Combine(snapshotsFolderPath, String.Format(CONVERT_SNAPSHOTS_SEGMENTS_DETECTED_ERRORS_FILTERED_FILE_NAME, Guid.NewGuid()));
                    FileIOHelper.writeListToCSVFile<DetectedError>(detectedErrorsFilteredList, new DetectedErrorReportMap(), detectedErrorsFilePathFiltered);
                    detectedErrorsFilePath = detectedErrorsFilePathFiltered;
                }
                else
                {
                    detectedErrorsFilePath = String.Empty;
                }

                if (businessDataFilteredList != null && businessDataFilteredList.Count > 0)
                {
                    businessDataFilePathFiltered = Path.Combine(snapshotsFolderPath, String.Format(CONVERT_SNAPSHOTS_SEGMENTS_BUSINESS_DATA_FILTERED_FILE_NAME, Guid.NewGuid()));
                    FileIOHelper.writeListToCSVFile<BusinessData>(businessDataFilteredList, new BusinessDataReportMap(), businessDataFilePathFiltered);
                    businessDataFilePath = businessDataFilePathFiltered;
                }
                else
                {
                    businessDataFilePath = String.Empty;
                }
            }

            #endregion

            #region Snapshots sheet

            if (snapshotsFilePath != String.Empty)
            {
                // Detail
                ExcelWorksheet sheetSnapshots = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS);
                sheetSnapshots.Cells[1, 1].Value = "Table of Contents";
                sheetSnapshots.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetSnapshots.Cells[2, 1].Value = "See Pivot";
                sheetSnapshots.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS_PIVOT);
                sheetSnapshots.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                // Pivot
                ExcelWorksheet sheetSnapshotsPivot = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS_PIVOT);
                sheetSnapshotsPivot.Cells[1, 1].Value = "Table of Contents";
                sheetSnapshotsPivot.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetSnapshotsPivot.Cells[2, 1].Value = "See Table";
                sheetSnapshotsPivot.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS);
                sheetSnapshotsPivot.View.FreezePanes(REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(snapshotsFilePath, 0, sheetSnapshots, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetSnapshots.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_SNAPSHOTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetSnapshots.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetSnapshots.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetSnapshots.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheetSnapshots.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheetSnapshots.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheetSnapshots.Column(table.Columns["UserExperience"].Position + 1).Width = 10;
                    sheetSnapshots.Column(table.Columns["RequestID"].Position + 1).Width = 20;
                    sheetSnapshots.Column(table.Columns["Occured"].Position + 1).AutoFit();
                    sheetSnapshots.Column(table.Columns["OccuredUtc"].Position + 1).AutoFit();
                    sheetSnapshots.Column(table.Columns["DetailLink"].Position + 1).AutoFit();

                    tableSnapshots = table;

                    ExcelPivotTable pivot = sheetSnapshotsPivot.PivotTables.Add(sheetSnapshotsPivot.Cells[REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_ENTITY_DETAILS_PIVOT_SNAPSHOTS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["UserExperience"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }
            }

            #endregion

            #region Segments sheet

            if (segmentsFilePath != String.Empty)
            {
                // Detail
                ExcelWorksheet sheetSegments = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_SEGMENTS);
                sheetSegments.Cells[1, 1].Value = "Table of Contents";
                sheetSegments.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetSegments.Cells[2, 1].Value = "See Pivot";
                sheetSegments.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_SEGMENTS_PIVOT);
                sheetSegments.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                // Pivot
                ExcelWorksheet sheetSegmentsPivot = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_SEGMENTS_PIVOT);
                sheetSegmentsPivot.Cells[1, 1].Value = "Table of Contents";
                sheetSegmentsPivot.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetSegmentsPivot.Cells[2, 1].Value = "See Table";
                sheetSegmentsPivot.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_SEGMENTS);
                sheetSegmentsPivot.View.FreezePanes(REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(segmentsFilePath, 0, sheetSegments, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetSegments.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_SEGMENTS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetSegments.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetSegments.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetSegments.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheetSegments.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheetSegments.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheetSegments.Column(table.Columns["UserExperience"].Position + 1).Width = 10;
                    sheetSegments.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheetSegments.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheetSegments.Column(table.Columns["ParentSegmentID"].Position + 1).Width = 15;
                    sheetSegments.Column(table.Columns["ParentTierName"].Position + 1).Width = 20;
                    sheetSegments.Column(table.Columns["Occured"].Position + 1).AutoFit();
                    sheetSegments.Column(table.Columns["OccuredUtc"].Position + 1).AutoFit();

                    ExcelPivotTable pivot = sheetSegmentsPivot.PivotTables.Add(sheetSegmentsPivot.Cells[REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_ENTITY_DETAILS_PIVOT_SEGMENTS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableField fieldC = pivot.ColumnFields.Add(pivot.Fields["UserExperience"]);
                    fieldC.Compact = false;
                    fieldC.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["SegmentID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }
            }

            #endregion

            #region Exit Calls sheet

            if (exitCallsFilePath != String.Empty)
            {
                // Detail
                ExcelWorksheet sheetExitCalls = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_EXIT_CALLS);
                sheetExitCalls.Cells[1, 1].Value = "Table of Contents";
                sheetExitCalls.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetExitCalls.Cells[2, 1].Value = "See Pivot";
                sheetExitCalls.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_EXIT_CALLS_PIVOT);
                sheetExitCalls.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                // Pivot
                ExcelWorksheet sheetExitCallsPivot = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_EXIT_CALLS_PIVOT);
                sheetExitCallsPivot.Cells[1, 1].Value = "Table of Contents";
                sheetExitCallsPivot.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetExitCallsPivot.Cells[2, 1].Value = "See Table";
                sheetExitCallsPivot.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_EXIT_CALLS);
                sheetExitCallsPivot.View.FreezePanes(REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT + 2, 7);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(exitCallsFilePath, 0, sheetExitCalls, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetExitCalls.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_EXIT_CALLS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetExitCalls.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetExitCalls.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetExitCalls.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheetExitCalls.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheetExitCalls.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheetExitCalls.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheetExitCalls.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheetExitCalls.Column(table.Columns["ToEntityName"].Position + 1).Width = 15;
                    sheetExitCalls.Column(table.Columns["ExitType"].Position + 1).Width = 10;
                    sheetExitCalls.Column(table.Columns["Detail"].Position + 1).Width = 20;
                    sheetExitCalls.Column(table.Columns["Method"].Position + 1).Width = 20;
                
                    ExcelPivotTable pivot = sheetExitCallsPivot.PivotTables.Add(sheetExitCallsPivot.Cells[REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_ENTITY_DETAILS_PIVOT_EXIT_CALLS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ExitType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["Detail"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                    fieldD = pivot.DataFields.Add(pivot.Fields["Duration"]);
                    fieldD.Function = DataFieldFunctions.Sum;
                }
            }

            #endregion

            #region Service Endpoint Calls sheet

            if (serviceEndpointCallsFilePath != String.Empty)
            {
                // Detail
                ExcelWorksheet sheetServiceEndpointCalls = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_SERVICE_ENDPOINT_CALLS);
                sheetServiceEndpointCalls.Cells[1, 1].Value = "Table of Contents";
                sheetServiceEndpointCalls.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetServiceEndpointCalls.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(serviceEndpointCallsFilePath, 0, sheetServiceEndpointCalls, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetServiceEndpointCalls.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_SERVICE_ENDPOINT_CALLS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetServiceEndpointCalls.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetServiceEndpointCalls.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetServiceEndpointCalls.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheetServiceEndpointCalls.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheetServiceEndpointCalls.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheetServiceEndpointCalls.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheetServiceEndpointCalls.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheetServiceEndpointCalls.Column(table.Columns["SepName"].Position + 1).Width = 20;
                }
            }

            #endregion

            #region Detected Errors sheet

            if (detectedErrorsFilePath != String.Empty)
            {
                // Detail
                ExcelWorksheet sheetDetectedErrors = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_DETECTED_ERRORS);
                sheetDetectedErrors.Cells[1, 1].Value = "Table of Contents";
                sheetDetectedErrors.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetDetectedErrors.Cells[2, 1].Value = "See Pivot";
                sheetDetectedErrors.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_DETECTED_ERRORS_PIVOT);
                sheetDetectedErrors.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                // Pivot
                ExcelWorksheet sheetDetectedErrorsPivot = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_DETECTED_ERRORS_PIVOT);
                sheetDetectedErrorsPivot.Cells[1, 1].Value = "Table of Contents";
                sheetDetectedErrorsPivot.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetDetectedErrorsPivot.Cells[2, 1].Value = "See Table";
                sheetDetectedErrorsPivot.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_ENTITY_DETAILS_SHEET_DETECTED_ERRORS);
                sheetDetectedErrorsPivot.View.FreezePanes(REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT + 2, 6);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(detectedErrorsFilePath, 0, sheetDetectedErrors, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetDetectedErrors.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_DETECTED_ERRORS);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetDetectedErrors.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetDetectedErrors.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetDetectedErrors.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheetDetectedErrors.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheetDetectedErrors.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheetDetectedErrors.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheetDetectedErrors.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheetDetectedErrors.Column(table.Columns["ErrorName"].Position + 1).Width = 20;
                    sheetDetectedErrors.Column(table.Columns["ErrorMessage"].Position + 1).Width = 20;
                    sheetDetectedErrors.Column(table.Columns["ErrorDetail"].Position + 1).Width = 20;

                    ExcelPivotTable pivot = sheetDetectedErrorsPivot.PivotTables.Add(sheetDetectedErrorsPivot.Cells[REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_ENTITY_DETAILS_PIVOT_DETECTED_ERRORS);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ErrorMessage"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }
            }

            #endregion

            #region Business Data sheet

            if (businessDataFilePath != String.Empty)
            {
                // Detail
                ExcelWorksheet sheetBusinessData = excelEntityDetail.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA);
                sheetBusinessData.Cells[1, 1].Value = "Table of Contents";
                sheetBusinessData.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetBusinessData.Cells[2, 1].Value = "See Pivot";
                sheetBusinessData.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA_PIVOT);
                sheetBusinessData.View.FreezePanes(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT + 1, 1);

                // Pivot
                ExcelWorksheet sheetBusinessDataPivot = excelEntityDetail.Workbook.Worksheets.Add(REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA_PIVOT);
                sheetBusinessDataPivot.Cells[1, 1].Value = "Table of Contents";
                sheetBusinessDataPivot.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
                sheetBusinessDataPivot.Cells[2, 1].Value = "See Table";
                sheetBusinessDataPivot.Cells[2, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SNAPSHOTS_SHEET_BUSINESS_DATA);
                sheetBusinessDataPivot.View.FreezePanes(REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT + 2, 7);

                fromRow = REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT;
                range = readCSVFileIntoExcelRange(businessDataFilePath, 0, sheetBusinessData, fromRow, 1);
                if (range != null && range.Rows > 1)
                {
                    table = sheetBusinessData.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_BUSINESS_DATA);
                    table.ShowHeader = true;
                    table.TableStyle = TableStyles.Medium2;
                    table.ShowFilter = true;
                    table.ShowTotal = false;

                    sheetBusinessData.Column(table.Columns["Controller"].Position + 1).Width = 20;
                    sheetBusinessData.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                    sheetBusinessData.Column(table.Columns["TierName"].Position + 1).Width = 20;
                    sheetBusinessData.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                    sheetBusinessData.Column(table.Columns["BTName"].Position + 1).Width = 20;
                    sheetBusinessData.Column(table.Columns["RequestID"].Position + 1).Width = 15;
                    sheetBusinessData.Column(table.Columns["SegmentID"].Position + 1).Width = 15;
                    sheetBusinessData.Column(table.Columns["DataName"].Position + 1).Width = 20;
                    sheetBusinessData.Column(table.Columns["DataValue"].Position + 1).Width = 20;
                    sheetBusinessData.Column(table.Columns["DataType"].Position + 1).Width = 10;
                    
                    ExcelPivotTable pivot = sheetBusinessDataPivot.PivotTables.Add(sheetBusinessDataPivot.Cells[REPORT_ENTITY_DETAILS_PIVOT_SHEET_START_PIVOT_AT, 1], range, REPORT_ENTITY_DETAILS_PIVOT_BUSINESS_DATA);
                    ExcelPivotTableField fieldR = pivot.RowFields.Add(pivot.Fields["Controller"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["ApplicationName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["TierName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["BTName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["DataType"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    fieldR = pivot.RowFields.Add(pivot.Fields["DataName"]);
                    fieldR.Compact = false;
                    fieldR.Outline = false;
                    ExcelPivotTableDataField fieldD = pivot.DataFields.Add(pivot.Fields["RequestID"]);
                    fieldD.Function = DataFieldFunctions.Count;
                }
            }

            #endregion

            #region Clear out temporary files

            if (snapshotsFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(snapshotsFilePathFiltered);
            }
            if (segmentsFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(segmentsFilePathFiltered);
            }
            if (exitCallsFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(exitCallsFilePathFiltered);
            }
            if (serviceEndpointCallsFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(serviceEndpointCallsFilePathFiltered);
            }
            if (detectedErrorsFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(detectedErrorsFilePathFiltered);
            }
            if (businessDataFilePathFiltered != String.Empty)
            {
                FileIOHelper.deleteFile(businessDataFilePathFiltered);
            }

            #endregion

            #endregion

            #region Detail sheet with Graphs, Snapshots and Events

            ExcelWorksheet sheetDetails = excelEntityDetail.Workbook.Worksheets.Add(REPORT_ENTITY_DETAILS_SHEET_HOURLY_TIMELINE);
            sheetDetails.Cells[1, 1].Value = "Table of Contents";
            sheetDetails.Cells[1, 2].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", REPORT_SHEET_TOC);
            sheetDetails.Cells[2, 1].Value = "Entity Type";
            sheetDetails.Cells[2, 2].Value = entityTypeForDisplay;
            sheetDetails.Cells[3, 1].Value = "Entity Name";
            sheetDetails.Cells[3, 2].Value = entityNameForDisplay;
            sheetDetails.View.FreezePanes(REPORT_ENTITY_DETAILS_GRAPHS_SHEET_START_TABLE_AT + 1, 6);

            sheetDetails.Column(1).Width = 10;
            sheetDetails.Column(2).Width = 10;
            sheetDetails.Column(3).Width = 10;
            sheetDetails.Column(4).Width = 12;
            sheetDetails.Column(5).Width = 12;

            #region Load Metric Data

            // Load metric data for each of the tables because it is faster than enumerating it from Excel sheet
            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_ART_SHORTNAME);
            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            List<MetricValue> metricValuesAPMList = FileIOHelper.readListFromCSVFile<MetricValue>(entityMetricValuesReportFilePath, new MetricValueMetricReportMap());

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_CPM_SHORTNAME);
            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            List<MetricValue> metricValuesCPMList = FileIOHelper.readListFromCSVFile<MetricValue>(entityMetricValuesReportFilePath, new MetricValueMetricReportMap());

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EPM_SHORTNAME);
            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            List<MetricValue> metricValuesEPMList = FileIOHelper.readListFromCSVFile<MetricValue>(entityMetricValuesReportFilePath, new MetricValueMetricReportMap());

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_EXCPM_SHORTNAME);
            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            List<MetricValue> metricValuesEXCPMList = FileIOHelper.readListFromCSVFile<MetricValue>(entityMetricValuesReportFilePath, new MetricValueMetricReportMap());

            metricsDataFolderPath = Path.Combine(metricsEntityFolderPath, METRIC_HTTPEPM_SHORTNAME);
            entityMetricValuesReportFilePath = Path.Combine(metricsDataFolderPath, CONVERT_METRIC_VALUES_FILE_NAME);
            List<MetricValue> metricValuesHTTPEPMList = FileIOHelper.readListFromCSVFile<MetricValue>(entityMetricValuesReportFilePath, new MetricValueMetricReportMap());

            // Break up the metric data into timeranges for each hour
            int[,] timeRangeAPM = null;
            int[,] timeRangeCPM = null;
            int[,] timeRangeEPM = null;
            int[,] timeRangeEXCPM = null;
            int[,] timeRangeHTTPEPM = null;
            if (metricValuesAPMList != null)
            {
                timeRangeAPM = getLocationsOfHourlyTimeRangesFromMetricValues(metricValuesAPMList, jobConfiguration.Input.HourlyTimeRanges);
            }
            if (metricValuesCPMList != null)
            {
                timeRangeCPM = getLocationsOfHourlyTimeRangesFromMetricValues(metricValuesCPMList, jobConfiguration.Input.HourlyTimeRanges);
            }
            if (metricValuesEPMList != null)
            {
                timeRangeEPM = getLocationsOfHourlyTimeRangesFromMetricValues(metricValuesEPMList, jobConfiguration.Input.HourlyTimeRanges);
            }
            if (metricValuesEXCPMList != null)
            {
                timeRangeEXCPM = getLocationsOfHourlyTimeRangesFromMetricValues(metricValuesEXCPMList, jobConfiguration.Input.HourlyTimeRanges);
            }
            if (metricValuesHTTPEPMList != null)
            {
                timeRangeHTTPEPM = getLocationsOfHourlyTimeRangesFromMetricValues(metricValuesHTTPEPMList, jobConfiguration.Input.HourlyTimeRanges);
            }

            #endregion

            #region Prepare Styles and Resize Columns for each of the hour ranges

            var minuteHeadingtyle = sheetDetails.Workbook.Styles.CreateNamedStyle("MinuteHeadingStyle");
            minuteHeadingtyle.Style.Font.Size = 9;

            var eventHeadingStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("EventHeadingStyle");
            eventHeadingStyle.Style.Font.Size = 9;

            var infoEventLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("InfoEventLinkStyle");
            infoEventLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            //infoEventLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
            infoEventLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0x0, 0x70, 0xC0)); // This is sort of Color.LightBlue, but I like it better
            infoEventLinkStyle.Style.Font.Color.SetColor(Color.White);
            infoEventLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var warnEventLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("WarnEventLinkStyle");
            warnEventLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            //warnEventLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.Orange);
            warnEventLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0xFF, 0xC0, 0x0)); // This is sort of Color.Orange, but I like it better
            warnEventLinkStyle.Style.Font.Color.SetColor(Color.Black);
            warnEventLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var errorEventLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("ErrorEventLinkStyle");
            errorEventLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            //errorEventLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.IndianRed);
            errorEventLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0xFF, 0x69, 0x69));
            errorEventLinkStyle.Style.Font.Color.SetColor(Color.Black);
            errorEventLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var normalSnapshotLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("NormalSnapshotLinkStyle");
            normalSnapshotLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            normalSnapshotLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0x00, 0x99, 0x0));
            normalSnapshotLinkStyle.Style.Font.Color.SetColor(Color.White);
            normalSnapshotLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var slowSnapshotLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("SlowSnapshotLinkStyle");
            slowSnapshotLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            slowSnapshotLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.Yellow); 
            slowSnapshotLinkStyle.Style.Font.Color.SetColor(Color.Black);
            slowSnapshotLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var verySlowSnapshotLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("VerySlowSnapshotLinkStyle");
            verySlowSnapshotLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            verySlowSnapshotLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0xFF, 0xC0, 0x0)); // This is sort of Color.Orange, but I like it better
            verySlowSnapshotLinkStyle.Style.Font.Color.SetColor(Color.Black);
            verySlowSnapshotLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var stallSnapshotLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("StallSnapshotLinkStyle");
            stallSnapshotLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            stallSnapshotLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0x99, 0x33, 0xFF)); // This is sort of Color.Purple, but I like it better
            stallSnapshotLinkStyle.Style.Font.Color.SetColor(Color.White);
            stallSnapshotLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var errorSnapshotLinkStyle = sheetDetails.Workbook.Styles.CreateNamedStyle("ErrorSnapshotLinkStyle");
            errorSnapshotLinkStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
            //errorSnapshotLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.IndianRed);
            errorSnapshotLinkStyle.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0xFF, 0x69, 0x69));
            errorSnapshotLinkStyle.Style.Font.Color.SetColor(Color.Black);
            errorSnapshotLinkStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            #endregion

            #region Resize Columns for each of the hour ranges

            // Prepare vertical section for each of the hours
            int columnOffsetBegin = 6;
            int columnOffsetBetweenRanges = 1;
            for (int i = 0; i < jobConfiguration.Input.HourlyTimeRanges.Count; i++)
            {
                JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[i];

                // Adjust columns in sheet
                int columnIndexTimeRangeStart = columnOffsetBegin + i * columnOffsetBetweenRanges + i * 60;
                int columnIndexTimeRangeEnd = columnIndexTimeRangeStart + 60;
                int minuteNumber = 0;
                for (int columnIndex = columnIndexTimeRangeStart; columnIndex < columnIndexTimeRangeEnd; columnIndex++)
                {
                    sheetDetails.Column(columnIndex).Width = 2.5;
                    sheetDetails.Cells[REPORT_ENTITY_DETAILS_GRAPHS_SHEET_START_TABLE_AT, columnIndex].Value = minuteNumber;
                    sheetDetails.Cells[REPORT_ENTITY_DETAILS_GRAPHS_SHEET_START_TABLE_AT, columnIndex].StyleName = "MinuteHeadingStyle";

                    sheetDetails.Column(columnIndex).OutlineLevel = 1;
                    minuteNumber++;
                }

                sheetDetails.Cells[1, columnIndexTimeRangeStart + 40].Value = "From";
                sheetDetails.Cells[1, columnIndexTimeRangeStart + 50].Value = "To";
                sheetDetails.Cells[2, columnIndexTimeRangeStart + 37].Value = "Local";
                sheetDetails.Cells[2, columnIndexTimeRangeStart + 40].Value = jobTimeRange.From.ToLocalTime().ToString("G");
                sheetDetails.Cells[2, columnIndexTimeRangeStart + 50].Value = jobTimeRange.To.ToLocalTime().ToString("G");
                sheetDetails.Cells[3, columnIndexTimeRangeStart + 37].Value = "UTC";
                sheetDetails.Cells[3, columnIndexTimeRangeStart + 40].Value = jobTimeRange.From.ToString("G");
                sheetDetails.Cells[3, columnIndexTimeRangeStart + 50].Value = jobTimeRange.To.ToString("G");
            }

            #endregion

            #region Output Metric Graphs

            int rowIndexART = 1;
            int rowIndexCPM = 1;
            int rowIndexEPM = 1;
            int rowIndexEXCPM = 1;
            int rowIndexHTTPEPM = 1;

            for (int i = 0; i < jobConfiguration.Input.HourlyTimeRanges.Count; i++)
            {
                JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[i];

                int columnIndexTimeRangeStart = columnOffsetBegin + i * columnOffsetBetweenRanges + i * 60;

                ExcelChart chart = sheetDetails.Drawings.AddChart(String.Format(REPORT_ENTITY_DETAILS_GRAPH, entityType, jobTimeRange.From), eChartType.XYScatterLinesNoMarkers);
                chart.SetPosition(REPORT_ENTITY_DETAILS_LIST_SHEET_START_TABLE_AT, 0, columnIndexTimeRangeStart - 1, 0);
                chart.SetSize(1020, 200);
                chart.Style = eChartStyle.Style17;

                ExcelChart chartART = chart.PlotArea.ChartTypes.Add(eChartType.XYScatterLinesNoMarkers);
                chartART.UseSecondaryAxis = true;

                // ART
                if (tableValuesART != null)
                {
                    ExcelRangeBase rangeXTime = getRangeMetricDataTableForThisHourDateTimeSeries(tableValuesART, timeRangeAPM[i, 0], timeRangeAPM[i, 1]);
                    ExcelRangeBase rangeYValues = getRangeMetricDataTableForThisHourValueSeries(tableValuesART, timeRangeAPM[i, 0], timeRangeAPM[i, 1]);
                    if (rangeXTime != null & rangeYValues != null)
                    {
                        ExcelChartSerie series = chartART.Series.Add(rangeYValues, rangeXTime);
                        series.Header = "ART";
                        ((ExcelScatterChartSerie)series).LineColor = Color.Green;

                        rowIndexART = rowIndexART + rangeXTime.Rows - 1;
                    }
                }

                // CPM
                if (tableValuesCPM != null)
                {
                    ExcelRangeBase rangeXTime = getRangeMetricDataTableForThisHourDateTimeSeries(tableValuesCPM, timeRangeCPM[i, 0], timeRangeCPM[i, 1]);
                    ExcelRangeBase rangeYValues = getRangeMetricDataTableForThisHourValueSeries(tableValuesCPM, timeRangeCPM[i, 0], timeRangeCPM[i, 1]);
                    if (rangeXTime != null & rangeYValues != null)
                    {
                        ExcelChartSerie series = chart.Series.Add(rangeYValues, rangeXTime);
                        series.Header = "CPM";
                        ((ExcelScatterChartSerie)series).LineColor = Color.Blue;

                        rowIndexCPM = rowIndexCPM + rangeXTime.Rows - 1;
                    }
                }

                // EPM
                if (tableValuesEPM != null)
                {
                    ExcelRangeBase rangeXTime = getRangeMetricDataTableForThisHourDateTimeSeries(tableValuesEPM, timeRangeEPM[i, 0], timeRangeEPM[i, 1]);
                    ExcelRangeBase rangeYValues = getRangeMetricDataTableForThisHourValueSeries(tableValuesEPM, timeRangeEPM[i, 0], timeRangeEPM[i, 1]);
                    if (rangeXTime != null & rangeYValues != null)
                    {
                        ExcelChartSerie series = chart.Series.Add(rangeYValues, rangeXTime);
                        series.Header = "EPM";
                        ((ExcelScatterChartSerie)series).LineColor = Color.Red;

                        rowIndexEPM = rowIndexEPM + rangeXTime.Rows - 1;
                    }
                }

                // EXCPM
                if (tableValuesEXCPM != null)
                {
                    ExcelRangeBase rangeXTime = getRangeMetricDataTableForThisHourDateTimeSeries(tableValuesEXCPM, timeRangeEXCPM[i, 0], timeRangeEXCPM[i, 1]);
                    ExcelRangeBase rangeYValues = getRangeMetricDataTableForThisHourValueSeries(tableValuesEXCPM, timeRangeEXCPM[i, 0], timeRangeEXCPM[i, 1]);
                    if (rangeXTime != null & rangeYValues != null)
                    {
                        ExcelChartSerie series = chart.Series.Add(rangeYValues, rangeXTime);
                        series.Header = "EXCPM";
                        ((ExcelScatterChartSerie)series).LineColor = Color.Orange;

                        rowIndexEXCPM = rowIndexEXCPM + rangeXTime.Rows - 1;
                    }
                }

                // HTTPEPM
                if (tableValuesHTTPEPM != null)
                {
                    ExcelRangeBase rangeXTime = getRangeMetricDataTableForThisHourDateTimeSeries(tableValuesHTTPEPM, timeRangeHTTPEPM[i, 0], timeRangeHTTPEPM[i, 1]);
                    ExcelRangeBase rangeYValues = getRangeMetricDataTableForThisHourValueSeries(tableValuesHTTPEPM, timeRangeHTTPEPM[i, 0], timeRangeHTTPEPM[i, 1]);
                    if (rangeXTime != null & rangeYValues != null)
                    {
                        ExcelChartSerie series = chart.Series.Add(rangeYValues, rangeXTime);
                        series.Header = "HTTPEPM";
                        ((ExcelScatterChartSerie)series).LineColor = Color.Pink;

                        rowIndexHTTPEPM = rowIndexHTTPEPM + rangeXTime.Rows - 1;
                    }
                }
            }

            #endregion

            #region Output Events

            fromRow = REPORT_ENTITY_DETAILS_GRAPHS_SHEET_START_TABLE_AT + 1;

            int rowTableStart = fromRow;
            if (eventsFilteredList != null)
            {
                // Group by type and subtype to break the overall list in manageable chunks
                var eventsAllGroupedByType = eventsFilteredList.GroupBy(e => new { e.Type, e.SubType });
                List<List<Event>> eventsAllGrouped = new List<List<Event>>();

                // Group by the additional columns for some of the events
                foreach (var eventsGroup in eventsAllGroupedByType)
                {
                    switch (eventsGroup.Key.Type)
                    {
                        case "RESOURCE_POOL_LIMIT":
                            var eventsGroup_RESOURCE_POOL_LIMIT = eventsGroup.ToList().GroupBy(e => e.TierName);
                            foreach (var eventsGrouping in eventsGroup_RESOURCE_POOL_LIMIT)
                            {
                                eventsAllGrouped.Add(eventsGrouping.ToList());
                            }
                            break;

                        case "APPLICATION_ERROR":
                            var eventsGroup_APPLICATION_ERROR = eventsGroup.ToList().GroupBy(e => e.TierName);
                            foreach (var eventsGrouping in eventsGroup_APPLICATION_ERROR)
                            {
                                eventsAllGrouped.Add(eventsGrouping.ToList());
                            }
                            break;

                        case "DIAGNOSTIC_SESSION":
                            var eventsGroup_DIAGNOSTIC_SESSION = eventsGroup.ToList().GroupBy(e => new { e.TierName, e.BTName });
                            foreach (var eventsGrouping in eventsGroup_DIAGNOSTIC_SESSION)
                            {
                                eventsAllGrouped.Add(eventsGrouping.ToList());
                            }
                            break;

                        case "CUSTOM":
                            var eventsGroup_CUSTOM = eventsGroup.ToList().GroupBy(e => e.TierName);
                            foreach (var eventsGrouping in eventsGroup_CUSTOM)
                            {
                                eventsAllGrouped.Add(eventsGrouping.ToList());
                            }
                            break;

                        case "POLICY_OPEN_WARNING":
                        case "POLICY_OPEN_CRITICAL":
                        case "POLICY_CLOSE_WARNING":
                        case "POLICY_CLOSE_CRITICAL":
                        case "POLICY_UPGRADED":
                        case "POLICY_DOWNGRADED":
                        case "POLICY_CANCELED_WARNING":
                        case "POLICY_CANCELED_CRITICAL":
                        case "POLICY_CONTINUES_CRITICAL":
                        case "POLICY_CONTINUES_WARNING":
                            var eventsGroup_POLICY_ALL = eventsGroup.ToList().GroupBy(e => new { e.TriggeredEntityName, e.TierName });
                            foreach (var eventsGrouping in eventsGroup_POLICY_ALL)
                            {
                                eventsAllGrouped.Add(eventsGrouping.ToList());
                            }
                            break;

                        default:
                            eventsAllGrouped.Add(eventsGroup.ToList());
                            break;
                    }
                }

                // At this point we have the events partitioned just the way we want them
                // Each entry is guaranteed to have at least one item

                sheetDetails.Cells[fromRow, 1].Value = "Type";
                sheetDetails.Cells[fromRow, 2].Value = "SubType";
                sheetDetails.Cells[fromRow, 3].Value = "Tier";
                sheetDetails.Cells[fromRow, 4].Value = "BT";
                sheetDetails.Cells[fromRow, 5].Value = "Trigger";

                fromRow++;
                for (int i = 0; i < eventsAllGrouped.Count; i++)
                {
                    int toRow = fromRow;

                    // Go through each hour range at a time
                    for (int j = 0; j < jobConfiguration.Input.HourlyTimeRanges.Count; j++)
                    {
                        JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[j];

                        List<Event> eventsInThisTimeRangeList = eventsAllGrouped[i].Where(e => e.OccuredUtc >= jobTimeRange.From && e.OccuredUtc < jobTimeRange.To).ToList();

                        // Now we finally have all the events for this type in this hour. Output
                        int columnIndexTimeRangeStart = columnOffsetBegin + j * columnOffsetBetweenRanges + j * 60;
                        foreach (Event interestingEvent in eventsInThisTimeRangeList)
                        {
                            // Find Column
                            int columnInThisTimeRange = columnIndexTimeRangeStart + interestingEvent.OccuredUtc.Minute;
                            // Find Row
                            int rowToOutputThisEventTo = fromRow;
                            while (true)
                            {
                                if (sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Value == null && sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula == String.Empty)
                                {
                                    break;
                                }
                                else
                                {
                                    rowToOutputThisEventTo++;
                                }
                            }
                            if (rowToOutputThisEventTo > fromRow && rowToOutputThisEventTo > toRow)
                            {
                                toRow = rowToOutputThisEventTo;
                            }

                            int rowIndexOfThisEvent = eventsFilteredList.FindIndex(e => e.EventID == interestingEvent.EventID);

                            // Finally output the value
                            switch (interestingEvent.Severity)
                            {
                                case "INFO":
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""I"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS, getRangeEventDataTableThisEvent(tableEvents, rowIndexOfThisEvent));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "InfoEventLinkStyle";
                                    break;
                                case "WARN":
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""W"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS, getRangeEventDataTableThisEvent(tableEvents, rowIndexOfThisEvent));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "WarnEventLinkStyle";
                                    break;
                                case "ERROR":
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""E"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS, getRangeEventDataTableThisEvent(tableEvents, rowIndexOfThisEvent));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "ErrorEventLinkStyle";
                                    break;
                                default:
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""?"")", REPORT_ENTITY_DETAILS_SHEET_EVENTS, getRangeEventDataTableThisEvent(tableEvents, rowIndexOfThisEvent));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "ErrorEventLinkStyle";
                                    break;
                            }

                            // Add tooltip
                            ExcelComment comment = sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].AddComment(interestingEvent.Summary, interestingEvent.EventID.ToString());
                            comment.AutoFit = true;

                            // Is there more than one event in this time range
                            if (rowToOutputThisEventTo > fromRow)
                            {
                                // Yes, then indicate that it has a few by underline
                                sheetDetails.Cells[fromRow, columnInThisTimeRange].Style.Font.UnderLine = true;
                            }
                        }
                    }

                    // Output headings in the event heading columns columns
                    Event firstEvent = eventsAllGrouped[i][0];
                    for (int j = fromRow; j <= toRow; j++)
                    {
                        sheetDetails.Cells[j, 1].Value = firstEvent.Type;
                        if (firstEvent.SubType != String.Empty) { sheetDetails.Cells[j, 2].Value = firstEvent.SubType; }
                        if (firstEvent.TierName != String.Empty) { sheetDetails.Cells[j, 3].Value = firstEvent.TierName; }
                        if (firstEvent.BTName != String.Empty) { sheetDetails.Cells[j, 4].Value = firstEvent.BTName; }
                        if (firstEvent.TriggeredEntityName != String.Empty) { sheetDetails.Cells[j, 5].Value = firstEvent.TriggeredEntityName; }
                        sheetDetails.Cells[j, 1].StyleName = "EventHeadingStyle";
                        sheetDetails.Cells[j, 2].StyleName = "EventHeadingStyle";
                        sheetDetails.Cells[j, 3].StyleName = "EventHeadingStyle";
                        sheetDetails.Cells[j, 4].StyleName = "EventHeadingStyle";
                        sheetDetails.Cells[j, 5].StyleName = "EventHeadingStyle";
                        if (j == fromRow)
                        {
                            sheetDetails.Row(j).OutlineLevel = 1;
                        }
                        else if (j > fromRow)
                        {
                            sheetDetails.Row(j).OutlineLevel = 2;
                        }
                    }

                    fromRow = toRow;
                    fromRow++;
                }
            }
            int rowTableEnd = fromRow - 1;

            if (rowTableStart < rowTableEnd)
            {
                // Insert the table
                range = sheetDetails.Cells[rowTableStart, 1, rowTableEnd, 5];
                table = sheetDetails.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_EVENTS_IN_TIMELINE);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.None;
                //table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;
            }

            #endregion

            #region Output Snapshots

            fromRow++;
            rowTableStart = fromRow;

            if (snapshotsFilteredList != null)
            {
                // Group by Tier and BT to break list into manageable chunks
                //var snapshotsAllGroupedByType = snapshotsFilteredList.GroupBy(s => new { s.TierName, s.BTName });
                var snapshotsAllGroupedByType = snapshotsFilteredList.OrderBy(s => s.TierName).ThenBy(s => s.BTName).GroupBy(s => new { s.TierName, s.BTName });
                List<List<Snapshot>> snapshotsAllGrouped = new List<List<Snapshot>>();

                // Group by the user experience
                foreach (var snapshotsGroup in snapshotsAllGroupedByType)
                {
                    List<Snapshot> snapshotsList = snapshotsGroup.ToList().Where(s => s.UserExperience == SNAPSHOT_UX_NORMAL).ToList();
                    if (snapshotsList.Count > 0) { snapshotsAllGrouped.Add(snapshotsList); }
                    snapshotsList = snapshotsGroup.ToList().Where(s => s.UserExperience == SNAPSHOT_UX_SLOW).ToList();
                    if (snapshotsList.Count > 0) { snapshotsAllGrouped.Add(snapshotsList); }
                    snapshotsList = snapshotsGroup.ToList().Where(s => s.UserExperience == SNAPSHOT_UX_VERY_SLOW).ToList();
                    if (snapshotsList.Count > 0) { snapshotsAllGrouped.Add(snapshotsList); }
                    snapshotsList = snapshotsGroup.ToList().Where(s => s.UserExperience == SNAPSHOT_UX_STALL).ToList();
                    if (snapshotsList.Count > 0) { snapshotsAllGrouped.Add(snapshotsList); }
                    snapshotsList = snapshotsGroup.ToList().Where(s => s.UserExperience == SNAPSHOT_UX_ERROR).ToList();
                    if (snapshotsList.Count > 0) { snapshotsAllGrouped.Add(snapshotsList); }
                }

                // At this point we have the snapshots partitioned just the way we want them
                // Each entry is guaranteed to have at least one item

                sheetDetails.Cells[fromRow, 1].Value = "Tier";
                sheetDetails.Cells[fromRow, 2].Value = "BT";
                sheetDetails.Cells[fromRow, 3].Value = " ";
                sheetDetails.Cells[fromRow, 4].Value = "  ";
                sheetDetails.Cells[fromRow, 5].Value = "Experience";

                fromRow++;
                for (int i = 0; i < snapshotsAllGrouped.Count; i++)
                {
                    int toRow = fromRow;

                    // Go through each hour range at a time
                    for (int j = 0; j < jobConfiguration.Input.HourlyTimeRanges.Count; j++)
                    {
                        JobTimeRange jobTimeRange = jobConfiguration.Input.HourlyTimeRanges[j];

                        List<Snapshot> snapshotsInThisTimeRangeList = snapshotsAllGrouped[i].Where(s => s.OccuredUtc >= jobTimeRange.From && s.OccuredUtc < jobTimeRange.To).ToList();

                        // Now we finally have all the events for this type in this hour. Output
                        int columnIndexTimeRangeStart = columnOffsetBegin + j * columnOffsetBetweenRanges + j * 60;
                        foreach (Snapshot interestingSnapshot in snapshotsInThisTimeRangeList)
                        {
                            // Find Column
                            int columnInThisTimeRange = columnIndexTimeRangeStart + interestingSnapshot.OccuredUtc.Minute;
                            // Find Row
                            int rowToOutputThisEventTo = fromRow;
                            while (true)
                            {
                                if (sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Value == null && sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula == String.Empty)
                                {
                                    break;
                                }
                                else
                                {
                                    rowToOutputThisEventTo++;
                                }
                            }
                            if (rowToOutputThisEventTo > fromRow && rowToOutputThisEventTo > toRow)
                            {
                                toRow = rowToOutputThisEventTo;
                            }

                            // Finally output the value
                            int rowIndexOfThisSnapshot = snapshotsFilteredList.FindIndex(s => s.RequestID == interestingSnapshot.RequestID);
                            switch (interestingSnapshot.UserExperience)
                            {
                                case SNAPSHOT_UX_NORMAL:
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""N"")", REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS, getRangeEventDataTableThisEvent(tableSnapshots, rowIndexOfThisSnapshot));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "NormalSnapshotLinkStyle";
                                    break;
                                case SNAPSHOT_UX_SLOW:
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""S"")", REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS, getRangeEventDataTableThisEvent(tableSnapshots, rowIndexOfThisSnapshot));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "SlowSnapshotLinkStyle";
                                    break;
                                case SNAPSHOT_UX_VERY_SLOW:
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""V"")", REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS, getRangeEventDataTableThisEvent(tableSnapshots, rowIndexOfThisSnapshot));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "VerySlowSnapshotLinkStyle";
                                    break;
                                case SNAPSHOT_UX_STALL:
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""X"")", REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS, getRangeEventDataTableThisEvent(tableSnapshots, rowIndexOfThisSnapshot));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "StallSnapshotLinkStyle";
                                    break;
                                case SNAPSHOT_UX_ERROR:
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].Formula = String.Format(@"=HYPERLINK(""#'{0}'!{1}"", ""E"")", REPORT_ENTITY_DETAILS_SHEET_SNAPSHOTS, getRangeEventDataTableThisEvent(tableSnapshots, rowIndexOfThisSnapshot));
                                    sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].StyleName = "ErrorSnapshotLinkStyle";
                                    break;
                                default:
                                    break;
                            }

                            // Add tooltip
                            ExcelComment comment = sheetDetails.Cells[rowToOutputThisEventTo, columnInThisTimeRange].AddComment(
                                String.Format("Duration: {0}\nURL: {1}\nSegments: {2}\nCall Graph: {3}\nCall Chain:\n{4}",
                                    interestingSnapshot.Duration,
                                    interestingSnapshot.URL,
                                    interestingSnapshot.NumSegments,
                                    interestingSnapshot.CallGraphType, 
                                    interestingSnapshot.CallChains),
                                interestingSnapshot.RequestID);
                            comment.AutoFit = true;

                            // Is there more than one event in this time range
                            if (rowToOutputThisEventTo > fromRow)
                            {
                                // Yes, then indicate that it has a few by underline
                                sheetDetails.Cells[fromRow, columnInThisTimeRange].Style.Font.UnderLine = true;
                            }
                        }

                    }

                    // Output headings in the event heading columns columns
                    Snapshot firstSnapshot = snapshotsAllGrouped[i][0];
                    for (int j = fromRow; j <= toRow; j++)
                    {
                        sheetDetails.Cells[j, 1].Value = firstSnapshot.TierName;
                        sheetDetails.Cells[j, 2].Value = firstSnapshot.BTName;
                        sheetDetails.Cells[j, 5].Value = firstSnapshot.UserExperience;
                        sheetDetails.Cells[j, 1].StyleName = "EventHeadingStyle";
                        sheetDetails.Cells[j, 2].StyleName = "EventHeadingStyle";
                        sheetDetails.Cells[j, 5].StyleName = "EventHeadingStyle";
                        if (j == fromRow)
                        {
                            sheetDetails.Row(j).OutlineLevel = 1;
                        }
                        else if (j > fromRow)
                        {
                            sheetDetails.Row(j).OutlineLevel = 2;
                        }
                    }

                    fromRow = toRow;
                    fromRow++;
                }
            }
            rowTableEnd = fromRow - 1;

            if (rowTableStart < rowTableEnd)
            {
                // Insert the table
                range = sheetDetails.Cells[rowTableStart, 1, rowTableEnd, 5];
                table = sheetDetails.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_SNAPSHOTS_IN_TIMELINE);
                table.ShowHeader = true;
                table.TableStyle = TableStyles.None;
                //table.TableStyle = TableStyles.Medium2;
                table.ShowFilter = true;
                table.ShowTotal = false;
            }

            sheetDetails.OutLineSummaryBelow = false;
            sheetDetails.OutLineSummaryRight = true;
            #endregion

            #endregion
        }

        private static bool finalizeAndSaveIndividualEntityMetricReport(ExcelPackage excelEntityDetail, string reportFilePath)
        {
            logger.Info("Finalize Entity Metrics Report File {0}", reportFilePath);

            excelEntityDetail.Workbook.Worksheets.MoveAfter(REPORT_ENTITY_DETAILS_SHEET_HOURLY_TIMELINE, REPORT_ENTITY_DETAILS_SHEET_SUMMARY);

            #region TOC sheet

            // TOC sheet again
            ExcelWorksheet sheet = excelEntityDetail.Workbook.Worksheets[REPORT_SHEET_TOC];
            sheet.Cells[1, 1].Value = "Sheet Name";
            sheet.Cells[1, 2].Value = "# Tables";
            sheet.Cells[1, 3].Value = "Link";
            int rowNum = 1;
            foreach (ExcelWorksheet s in excelEntityDetail.Workbook.Worksheets)
            {
                rowNum++;
                sheet.Cells[rowNum, 1].Value = s.Name;
                sheet.Cells[rowNum, 3].Formula = String.Format(@"=HYPERLINK(""#'{0}'!A1"", ""<Go>"")", s.Name);
                if (s.Tables.Count > 0)
                {
                    sheet.Cells[rowNum, 2].Value =  s.Tables[0].Address.Rows - 1;
                }
            }
            ExcelRangeBase range = sheet.Cells[1, 1, sheet.Dimension.Rows, sheet.Dimension.Columns];
            ExcelTable table = sheet.Tables.Add(range, REPORT_ENTITY_DETAILS_TABLE_TOC);
            table.ShowHeader = true;
            table.TableStyle = TableStyles.Medium2;
            table.ShowFilter = true;
            table.ShowTotal = false;

            sheet.Column(table.Columns["Sheet Name"].Position + 1).AutoFit();
            sheet.Column(table.Columns["# Tables"].Position + 1).AutoFit();

            #endregion
            
            #region Save file 

            // Report files
            logger.Info("Saving Excel report {0}", reportFilePath);
            //loggerConsole.Info("Saving Excel report {0}", reportFilePath);

            string folderPath = Path.GetDirectoryName(reportFilePath);
            if (Directory.Exists(folderPath) == false)
            {
                Directory.CreateDirectory(folderPath);
            }

            try
            {
                // Save full report Excel files
                excelEntityDetail.SaveAs(new FileInfo(reportFilePath));
            }
            catch (InvalidOperationException ex)
            {
                logger.Warn("Unable to save Excel file {0}", reportFilePath);
                logger.Warn(ex);
                loggerConsole.Warn("Unable to save Excel file {0}", reportFilePath);

                return false;
            }

            #endregion

            return true;
        }

        private static void adjustColumnsOfEntityRowTableInMetricReport(string entityType, ExcelWorksheet sheet, ExcelTable table)
        {
            if (entityType == APPLICATION_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
            }
            else if (entityType == TIERS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["AgentType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
            }
            else if (entityType == NODES_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["NodeName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["AgentType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["NodeLink"].Position + 1).AutoFit();
            }
            else if (entityType == BACKENDS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["BackendName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["BackendType"].Position + 1).Width = 20;
                sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["BackendLink"].Position + 1).AutoFit();
            }
            else if (entityType == BUSINESS_TRANSACTIONS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["BTName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["BTType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["BTLink"].Position + 1).AutoFit();
            }
            else if (entityType == SERVICE_ENDPOINTS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["SEPName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["SEPType"].Position + 1).AutoFit();
                sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["SEPLink"].Position + 1).AutoFit();
            }
            else if (entityType == ERRORS_TYPE_SHORT)
            {
                sheet.Column(table.Columns["Controller"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ApplicationName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["TierName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["ErrorName"].Position + 1).Width = 20;
                sheet.Column(table.Columns["From"].Position + 1).AutoFit();
                sheet.Column(table.Columns["To"].Position + 1).AutoFit();
                sheet.Column(table.Columns["FromUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["ToUtc"].Position + 1).AutoFit();
                sheet.Column(table.Columns["DetailLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ControllerLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ApplicationLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["TierLink"].Position + 1).AutoFit();
                //sheet.Column(table.Columns["ErrorLink"].Position + 1).AutoFit();
            }
        }

        private static int[,] getLocationsOfHourlyTimeRangesFromMetricValues(List<MetricValue> metricValues, List<JobTimeRange> jobTimeRanges)
        {
            // Element 0 of each row is index of time range Start
            // Element 1 of each row is index of time range End
            int[,] timeRangePartitionData = new int[jobTimeRanges.Count, 2];
            int fromIndexColumn = 0;
            int toIndexColumn = 1;
            int metricValuesCount = metricValues.Count;

            // First pass - scroll through and bucketize each our into invidual hour chunk
            int currentMetricValueRowIndex = 0;
            for (int i = 0; i < jobTimeRanges.Count; i++)
            {
                JobTimeRange jobTimeRange = jobTimeRanges[i];

                timeRangePartitionData[i, fromIndexColumn] = -1;
                timeRangePartitionData[i, toIndexColumn] = -1;

                while (currentMetricValueRowIndex < metricValuesCount)
                {
                    if (metricValues[currentMetricValueRowIndex].EventTimeStampUtc >= jobTimeRange.From &&
                        metricValues[currentMetricValueRowIndex].EventTimeStampUtc < jobTimeRange.To)
                    {
                        if (timeRangePartitionData[i, fromIndexColumn] == -1)
                        {
                            // Found From
                            timeRangePartitionData[i, fromIndexColumn] = currentMetricValueRowIndex;
                            timeRangePartitionData[i, toIndexColumn] = currentMetricValueRowIndex;
                        }
                        else
                        {
                            // Found potential To
                            timeRangePartitionData[i, toIndexColumn] = currentMetricValueRowIndex;
                        }
                        currentMetricValueRowIndex++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Second pass - adjust end times to overlap with the on-the-hour entries from subsequent ones because we're going to want those entries on the graphs
            // But don't adjust last entry 
            for (int i = 0; i < jobTimeRanges.Count - 1; i++)
            {
                JobTimeRange jobTimeRange = jobTimeRanges[i];

                if (timeRangePartitionData[i, fromIndexColumn] != -1 && 
                    timeRangePartitionData[i, toIndexColumn] != -1 &&
                    timeRangePartitionData[i + 1, fromIndexColumn] != -1 &&
                    timeRangePartitionData[i + 1, toIndexColumn] != -1)
                {
                    if (metricValues[timeRangePartitionData[i + 1, fromIndexColumn]].EventTimeStampUtc == jobTimeRange.To)
                    {
                        timeRangePartitionData[i, toIndexColumn] = timeRangePartitionData[i + 1, fromIndexColumn];
                    }
                }
            }

            return timeRangePartitionData;
        }

        private static ExcelRangeBase getRangeMetricDataTableForThisHourDateTimeSeries(ExcelTable table, int rowIndexStart, int rowIndexEnd)
        {
            // Find index of the important columns
            int columnIndexEventTime = table.Columns["EventTime"].Position;

            if (rowIndexStart != -1 && rowIndexEnd != -1)
            {
                return table.WorkSheet.Cells[
                    table.Address.Start.Row + rowIndexStart + 1,
                    table.Address.Start.Column + columnIndexEventTime,
                    table.Address.Start.Row + rowIndexEnd + 1,
                    table.Address.Start.Column + columnIndexEventTime];
            }

            return null;
        }

        private static ExcelRangeBase getRangeMetricDataTableForThisHourValueSeries(ExcelTable table, int rowIndexStart, int rowIndexEnd)
        {
            // Find index of the important columns
            int columnIndexEventTime = table.Columns["Value"].Position;

            if (rowIndexStart != -1 && rowIndexEnd != -1)
            {
                return table.WorkSheet.Cells[
                    table.Address.Start.Row + rowIndexStart + 1,
                    table.Address.Start.Column + columnIndexEventTime,
                    table.Address.Start.Row + rowIndexEnd + 1,
                    table.Address.Start.Column + columnIndexEventTime];
            }

            return null;
        }

        private static ExcelRangeBase getRangeEventDataTableThisEvent(ExcelTable table, int rowIndex)
        {
            return table.WorkSheet.Cells[
                table.Address.Start.Row + rowIndex + 1,
                table.Address.Start.Column,
                table.Address.Start.Row + rowIndex + 1,
                table.Address.End.Column];
        }

        #endregion


        #region Helper functions for reading CSV into Excel worksheet

        private static ExcelRangeBase readCSVFileIntoExcelRange(string csvFilePath, int skipLinesFromBeginning, ExcelWorksheet sheet, int startRow, int startColumn)
        {
            logger.Trace("Reading CSV file {0} to Excel Worksheet {1} at (row {2}, column {3})", csvFilePath, sheet.Name, startRow, startColumn);

            if (File.Exists(csvFilePath) == false)
            {
                logger.Warn("Unable to find file {0}", csvFilePath);

                return null;
            }

            try
            {
                int csvRowIndex = -1;
                int numColumnsInCSV = 0;
                string[] headerRowValues = null;

                using (StreamReader sr = File.OpenText(csvFilePath))
                {
                    CsvParser csvParser = new CsvParser(sr);

                    // Read all rows
                    while (true)
                    {
                        string[] rowValues = csvParser.Read();
                        if (rowValues == null)
                        {
                            break;
                        }
                        csvRowIndex++;

                        // Grab the headers
                        if (csvRowIndex == 0)
                        {
                            headerRowValues = rowValues;
                            numColumnsInCSV = headerRowValues.Length;
                        }

                        // Should we skip?
                        if (csvRowIndex < skipLinesFromBeginning)
                        {
                            // Skip this line
                            continue;
                        }

                        // Read row one field at a time
                        int csvFieldIndex = 0;
                        foreach (string fieldValue in rowValues)
                        {
                            ExcelRange cell = sheet.Cells[csvRowIndex + startRow - skipLinesFromBeginning, csvFieldIndex + startColumn];
                            if (fieldValue.StartsWith("=") == true)
                            {
                                cell.Formula = fieldValue;

                                if (fieldValue.StartsWith("=HYPERLINK") == true)
                                {
                                    cell.StyleName = "HyperLinkStyle";
                                }
                            }
                            else if (fieldValue.StartsWith("http://") == true || fieldValue.StartsWith("https://") == true)
                            {
                                // If it is in the column ending in Link, I want it to be hyperlinked and use the column name
                                if (headerRowValues[csvFieldIndex] == "Link")
                                {
                                    // This is the ART summary table, those links are OK, there are not that many of them
                                    cell.Hyperlink = new Uri(fieldValue);
                                    cell.Value = "<Go>";
                                    cell.StyleName = "HyperLinkStyle";
                                }
                                // Temporarily commenting out until I figure the large number of rows leading to hyperlink corruption thing
                                //else if (headerRowValues[csvFieldIndex].EndsWith("Link"))
                                //{
                                //    cell.Hyperlink = new Uri(fieldValue);
                                //    string linkName = String.Format("<{0}>", headerRowValues[csvFieldIndex].Replace("Link", ""));
                                //    if (linkName == "<>") linkName = "<Go>";
                                //    cell.Value = linkName;
                                //    cell.StyleName = "HyperLinkStyle";
                                //}
                                else
                                {
                                    // Otherwise dump it as text
                                    cell.Value = fieldValue;
                                }
                            }
                            else
                            {
                                Double numValue;
                                bool boolValue;
                                DateTime dateValue;

                                // Try some casting
                                if (Double.TryParse(fieldValue, NumberStyles.Any, NumberFormatInfo.InvariantInfo, out numValue) == true)
                                {
                                    // Number
                                    cell.Value = numValue;
                                }
                                else if (Boolean.TryParse(fieldValue, out boolValue) == true)
                                {
                                    // Boolean
                                    cell.Value = boolValue;
                                }
                                else if (DateTime.TryParse(fieldValue, out dateValue))
                                {
                                    // DateTime
                                    cell.Value = dateValue;
                                    if (headerRowValues[csvFieldIndex] == "EventTime")
                                    {
                                        cell.Style.Numberformat.Format = "hh:mm";
                                    }
                                    else
                                    {
                                        cell.Style.Numberformat.Format = "mm/dd/yyyy hh:mm:ss";
                                    }
                                }
                                else
                                {
                                    // Something else, dump as is
                                    cell.Value = fieldValue;
                                }
                            }
                            csvFieldIndex++;
                        }
                    }
                }

                return sheet.Cells[startRow, startColumn, startRow + csvRowIndex, startColumn + numColumnsInCSV - 1];
            }
            catch (Exception ex)
            {
                logger.Error("Unable to read CSV from file {0}", csvFilePath);
                logger.Error(ex);
            }

            return null;
        }

        #endregion

        #region Helper function for various entity naming

        private static string getShortenedEntityNameForFileSystem(string entityName, long entityID)
        {
            string originalEntityName = entityName;

            // First, strip out unsafe characters
            entityName = getFileSystemSafeString(entityName);

            // Second, shorten the string 
            if (entityName.Length > 12) entityName = entityName.Substring(0, 12);

            return String.Format("{0}.{1}", entityName, entityID);
        }

        private static string getFileSystemSafeString(string fileOrFolderNameToClear)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileOrFolderNameToClear = fileOrFolderNameToClear.Replace(c, '-');
            }

            return fileOrFolderNameToClear;
        }

        private static string getShortenedEntityNameForExcelTable(string entityName, long entityID)
        {
            string originalEntityName = entityName;

            // First, strip out unsafe characters
            entityName = getExcelTableOrSheetSafeString(entityName);

            // Second, shorten the string 
            if (entityName.Length > 50) entityName = entityName.Substring(0, 50);

            return String.Format("{0}.{1}", entityName, entityID);
        }

        private static string getShortenedEntityNameForExcelSheet(string entityName, int entityID, int maxLength)
        {
            string originalEntityName = entityName;

            // First, strip out unsafe characters
            entityName = getExcelTableOrSheetSafeString(entityName);

            // Second, measure the unique ID length and shorten the name of string down
            maxLength = maxLength - 1 - entityID.ToString().Length;

            // Third, shorten the string 
            if (entityName.Length > maxLength) entityName = entityName.Substring(0, maxLength);

            return String.Format("{0}.{1}", entityName, entityID);
        }

        private static string getExcelTableOrSheetSafeString(string stringToClear)
        {
            char[] excelTableInvalidChars = { ' ', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '-', '=', ',', '/', '\\', '[', ']', ':', '?', '|', '"', '<', '>'};
            foreach (var c in excelTableInvalidChars)
            {
                stringToClear = stringToClear.Replace(c, '-');
            }

            return stringToClear;
        }

        #endregion

        #region Helper functions for Unix time handling

        /// <summary>
        /// Converts UNIX timestamp to DateTime
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        private static DateTime convertFromUnixTimestamp(long timestamp)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return origin.AddMilliseconds(timestamp);
        }

        /// <summary>
        /// Converts DateTime to Unix timestamp
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        private static long convertToUnixTimestamp(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan diff = date.ToUniversalTime() - origin;
            return (long)Math.Floor(diff.TotalMilliseconds);
        }

        #endregion
    }
}