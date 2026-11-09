using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using WICareerBuilder.Core.Discovery;
using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;
using WICareerBuilder.Data.Repositories;
using WICareerBuilder.Desktop.Infrastructure;
using WICareerBuilder.Desktop.Viewers;
using WICareerBuilder.Documents.Analysis;
using WICareerBuilder.Learning.Activities;

namespace WICareerBuilder.Desktop.Interop;

public sealed class WebBridge
{
    private readonly WebView2 _webView;
    private readonly LibraryRepository _repository;
    private readonly ResourceDiscoveryService _discovery;
    private readonly ContentFingerprintService _fingerprintService;
    private readonly PdfResourceAnalyser _pdfAnalyser;
    private readonly StructuredResourceAnalyser _structuredAnalyser;
    private readonly LearningRepository _learningRepository;
    private readonly LearningActivityEvaluator _learningEvaluator;
    private readonly UpdateService _updateService;
    private readonly Window _owner;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public WebBridge(
        WebView2 webView,
        LibraryRepository repository,
        LearningRepository learningRepository,
        ResourceDiscoveryService discovery,
        ContentFingerprintService fingerprintService,
        PdfResourceAnalyser pdfAnalyser,
        StructuredResourceAnalyser structuredAnalyser,
        LearningActivityEvaluator learningEvaluator,
        UpdateService updateService,
        Window owner)
    {
        _webView = webView;
        _repository = repository;
        _discovery = discovery;
        _fingerprintService = fingerprintService;
        _pdfAnalyser = pdfAnalyser;
        _structuredAnalyser = structuredAnalyser;
        _learningRepository = learningRepository;
        _learningEvaluator = learningEvaluator;
        _updateService = updateService;
        _owner = owner;
    }

    public async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var uiPath = Path.Combine(AppContext.BaseDirectory, "UI", "index.html");
        if (!File.Exists(uiPath)) throw new FileNotFoundException("WICareerBuilder UI was not found.", uiPath);
        _webView.Source = new Uri(uiPath);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, _json);
            if (request is null) return;
            var data = await HandleAsync(request);
            await ReplyAsync(new BridgeResponse("response", request.RequestId, true, data));
        }
        catch (Exception ex)
        {
            SimpleLog.Write("Bridge error: " + ex);
            if (request is not null)
                await ReplyAsync(new BridgeResponse("response", request.RequestId, false, null, ex.Message));
        }
    }

    private async Task<object?> HandleAsync(BridgeRequest request)
    {
        switch (request.Type)
        {
            case "getDashboard":
                return await _repository.GetDashboardAsync();

            case "getWorkbenchSummary":
                return await _repository.GetWorkbenchSummaryAsync();

            case "getKnowledgeWorkbenchOverview":
                return await _repository.GetKnowledgeWorkbenchOverviewAsync();

            case "getResourceRoleSummary":
                return await _repository.GetResourceRoleSummaryAsync();

            case "assignResourceRole":
            {
                var roleKey = request.Payload.GetProperty("roleKey").GetString() ?? "";
                var status = request.Payload.TryGetProperty("status", out var roleStatus) ? roleStatus.GetString() ?? "Accepted" : "Accepted";
                return await _repository.AssignResourceRoleAsync(ReadResourceIds(request.Payload), roleKey, status);
            }

            case "getSourceAnalysisWorkbenchSummary":
                return await _repository.GetSourceAnalysisWorkbenchSummaryAsync();

            case "getPrototypeMaterialSummary":
                return await _repository.GetPrototypeMaterialSummaryAsync();

            case "getCareerProjects":
                return await _repository.GetCareerProjectsAsync();

            case "createCareerProject":
            {
                var p = request.Payload;
                var id = await _repository.CreateCareerProjectAsync(
                    p.GetProperty("name").GetString() ?? "",
                    p.TryGetProperty("category", out var category) ? category.GetString() ?? "General" : "General",
                    p.TryGetProperty("summary", out var projectSummary) ? projectSummary.GetString() ?? "" : "",
                    p.TryGetProperty("privacyMode", out var privacy) ? privacy.GetString() ?? "Private" : "Private",
                    !p.TryGetProperty("noWebAccess", out var noWeb) || noWeb.GetBoolean());
                return new { id };
            }

            case "getCareerProject":
                return await _repository.GetCareerProjectAsync(request.Payload.GetProperty("projectId").GetInt64());

            case "addProjectJournalEntry":
            {
                var p = request.Payload;
                var id = await _repository.AddProjectJournalEntryAsync(
                    p.GetProperty("projectId").GetInt64(),
                    p.TryGetProperty("entryType", out var entryType) ? entryType.GetString() ?? "Note" : "Note",
                    p.TryGetProperty("title", out var journalTitle) ? journalTitle.GetString() ?? "" : "",
                    p.TryGetProperty("body", out var journalBody) ? journalBody.GetString() ?? "" : "",
                    !p.TryGetProperty("isPrivate", out var journalPrivate) || journalPrivate.GetBoolean());
                return new { id };
            }

            case "getCareerSkills":
                return await _repository.GetCareerSkillsAsync();

            case "createCareerSkill":
            {
                var p = request.Payload;
                var id = await _repository.CreateCareerSkillAsync(p.GetProperty("name").GetString() ?? "", p.TryGetProperty("category", out var skillCategory) ? skillCategory.GetString() ?? "General" : "General", p.TryGetProperty("description", out var skillDescription) ? skillDescription.GetString() ?? "" : "");
                return new { id };
            }

            case "getCareerEvidence":
                return await _repository.GetCareerEvidenceAsync();

            case "createCareerEvidence":
            {
                var p = request.Payload;
                long? projectId = p.TryGetProperty("projectId", out var projectElement) && projectElement.ValueKind == JsonValueKind.Number ? projectElement.GetInt64() : null;
                var id = await _repository.CreateCareerEvidenceAsync(
                    p.GetProperty("title").GetString() ?? "",
                    p.TryGetProperty("evidenceKind", out var evidenceKind) ? evidenceKind.GetString() ?? "Document" : "Document",
                    p.TryGetProperty("evidenceStage", out var evidenceStage) ? evidenceStage.GetString() ?? "Mentioned" : "Mentioned",
                    p.TryGetProperty("description", out var evidenceDescription) ? evidenceDescription.GetString() ?? "" : "",projectId,null,
                    p.TryGetProperty("practicalEvidence", out var practical) && practical.GetBoolean(),
                    p.TryGetProperty("understandingDemonstrated", out var understanding) && understanding.GetBoolean(),
                    p.TryGetProperty("verificationStatus", out var verification) ? verification.GetString() ?? "Unverified" : "Unverified",
                    p.TryGetProperty("privacyMode", out var evidencePrivacy) ? evidencePrivacy.GetString() ?? "Private" : "Private",
                    !p.TryGetProperty("noWebAccess", out var evidenceNoWeb) || evidenceNoWeb.GetBoolean());
                return new { id };
            }

            case "linkEvidenceSkill":
            {
                var p = request.Payload;
                await _repository.LinkEvidenceSkillAsync(p.GetProperty("evidenceId").GetInt64(),p.GetProperty("skillId").GetInt64(),p.TryGetProperty("strength",out var strength)?strength.GetString()??"Supporting":"Supporting",p.TryGetProperty("notes",out var linkNotes)?linkNotes.GetString()??"":"");
                return new { saved = true };
            }


            case "getEvidenceProvenance":
                return await _repository.GetEvidenceProvenanceAsync(request.Payload.GetProperty("evidenceId").GetInt64());

            case "addEvidenceProvenance":
            {
                var p=request.Payload;
                var id=await _repository.AddEvidenceProvenanceAsync(
                    p.GetProperty("evidenceId").GetInt64(),
                    p.TryGetProperty("sourceKind",out var provenanceKind)?provenanceKind.GetString()??"Manual":"Manual",
                    p.TryGetProperty("sourceReference",out var provenanceRef)?provenanceRef.GetString()??"":"",
                    p.TryGetProperty("learningSubject",out var provenanceSubject)?provenanceSubject.GetString()??"":"",
                    p.TryGetProperty("topic",out var provenanceTopic)?provenanceTopic.GetString()??"":"",
                    p.TryGetProperty("subtopic",out var provenanceSubtopic)?provenanceSubtopic.GetString()??"":"",
                    p.TryGetProperty("notes",out var provenanceNotes)?provenanceNotes.GetString()??"":"");
                return new { id };
            }

            case "getCareerCredentials":
                return await _repository.GetCareerCredentialsAsync(request.Payload.TryGetProperty("credentialType",out var credentialType)?credentialType.GetString()??"":"");

            case "createCareerCredential":
            {
                var p=request.Payload;
                var id=await _repository.CreateCareerCredentialAsync(p.GetProperty("credentialType").GetString()??"",p.GetProperty("title").GetString()??"",p.TryGetProperty("issuer",out var issuer)?issuer.GetString()??"":"",p.TryGetProperty("status",out var credentialStatus)?credentialStatus.GetString()??"Recorded":"Recorded",p.TryGetProperty("awardDate",out var awardDate)?awardDate.GetString()??"":"",p.TryGetProperty("expiryDate",out var expiryDate)?expiryDate.GetString()??"":"",p.TryGetProperty("verificationStatus",out var credentialVerification)?credentialVerification.GetString()??"Unverified":"Unverified",p.TryGetProperty("userConfirmed",out var userConfirmed)&&userConfirmed.GetBoolean(),null,p.TryGetProperty("notes",out var credentialNotes)?credentialNotes.GetString()??"":"");
                return new { id };
            }

            case "getMasterEvidenceSummary":
                return await _repository.GetMasterEvidenceSummaryAsync();

            case "getCareerCvs":
                return await _repository.GetCareerCvsAsync();

            case "createCareerCv":
            {
                var p=request.Payload;var id=await _repository.CreateCareerCvAsync(p.GetProperty("name").GetString()??"",p.TryGetProperty("targetRole",out var targetRole)?targetRole.GetString()??"":"",p.TryGetProperty("focusKey",out var focusKey)?focusKey.GetString()??"General":"General");return new { id };
            }

            case "addCredentialToCv":
            {
                var p=request.Payload;await _repository.AddCredentialToCvAsync(p.GetProperty("cvId").GetInt64(),p.GetProperty("credentialId").GetInt64(),p.TryGetProperty("heading",out var credentialHeading)?credentialHeading.GetString()??"":"",p.TryGetProperty("statement",out var credentialStatement)?credentialStatement.GetString()??"":"");return new { saved=true };
            }

            case "getCareerCvItems":
                return await _repository.GetCareerCvItemsAsync(request.Payload.GetProperty("cvId").GetInt64());

            case "addEvidenceToCv":
            {
                var p=request.Payload;await _repository.AddEvidenceToCvAsync(p.GetProperty("cvId").GetInt64(),p.GetProperty("evidenceId").GetInt64(),p.TryGetProperty("heading",out var cvHeading)?cvHeading.GetString()??"":"",p.TryGetProperty("statement",out var cvStatement)?cvStatement.GetString()??"":"");return new { saved=true };
            }

            case "setEvidenceCvApproval":
            {
                await _repository.SetEvidenceCvApprovalAsync(request.Payload.GetProperty("evidenceId").GetInt64(),request.Payload.GetProperty("approved").GetBoolean());return new { saved=true };
            }

            case "setProjectCareerEligibility":
            {
                var p=request.Payload;await _repository.SetProjectCareerEligibilityAsync(p.GetProperty("projectId").GetInt64(),p.TryGetProperty("cvEligible",out var projectCvEligible)&&projectCvEligible.GetBoolean(),p.TryGetProperty("portfolioEligible",out var projectPortfolioEligible)&&projectPortfolioEligible.GetBoolean());return new { saved=true };
            }

            case "getCareerPortfolio":
                return await _repository.GetCareerPortfolioEntriesAsync();

            case "createCareerPortfolioEntry":
            {
                var p=request.Payload;var id=await _repository.CreateCareerPortfolioEntryAsync(p.GetProperty("projectId").GetInt64(),p.GetProperty("title").GetString()??"",p.TryGetProperty("problem",out var portfolioProblem)?portfolioProblem.GetString()??"":"",p.TryGetProperty("resultSummary",out var portfolioResult)?portfolioResult.GetString()??"":"");return new { id };
            }

            case "getCareerWorkspaceSummary":
                return await _repository.GetCareerWorkspaceSummaryAsync();


            case "authorizeExternalProcessing":
            {
                var p=request.Payload;
                return await _repository.AuthorizeExternalProcessingAsync(
                    p.GetProperty("sourceKind").GetString()??"",
                    p.GetProperty("sourceId").GetInt64(),
                    p.GetProperty("operation").GetString()??"");
            }

            case "getPrivacyAccessAudit":
            {
                var p=request.Payload;
                return await _repository.GetPrivacyAccessAuditAsync(p.GetProperty("sourceKind").GetString()??"",p.GetProperty("sourceId").GetInt64());
            }

            case "getSources":
                return await _repository.GetSourcesAsync();

            case "verifyLibrarySource":
                return await _repository.VerifyLibrarySourceAsync(request.Payload.GetProperty("sourceId").GetInt64());

            case "verifyDatabase":
                return await _repository.VerifyDatabaseAsync();

            case "getLatestDiscoveryScanAudit":
                return await _repository.GetLatestDiscoveryScanAuditAsync(request.Payload.GetProperty("sourceId").GetInt64());

            case "getLatestDiscoveryScanChanges":
            {
                var sourceId = request.Payload.GetProperty("sourceId").GetInt64();
                var changeType = request.Payload.TryGetProperty("changeType", out var changeElement) ? changeElement.GetString() ?? "" : "";
                var limit = request.Payload.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(limitElement.GetInt32(), 1, 1000)
                    : 500;
                return await _repository.GetLatestDiscoveryScanChangesAsync(sourceId, changeType, limit);
            }

            case "pickFolder":
            {
                var dialog = new OpenFolderDialog { Title = "Choose a WICareerBuilder library folder", Multiselect = false };
                return dialog.ShowDialog(_owner) == true ? new { path = dialog.FolderName } : new { path = "" };
            }

            case "addSource":
            {
                var payload = request.Payload;
                var path = payload.GetProperty("path").GetString()?.Trim() ?? "";
                var name = payload.GetProperty("name").GetString()?.Trim() ?? "";
                var subject = payload.GetProperty("subject").GetString()?.Trim() ?? "Engineering";
                var courseLevel = payload.TryGetProperty("courseLevel", out var cl) ? cl.GetString()?.Trim() ?? "" : "";
                var exclusions = payload.TryGetProperty("exclusions", out var ex)
                    ? ex.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray()
                    : [];
                if (path.Length == 0 || name.Length == 0 || subject.Length == 0)
                    throw new InvalidOperationException("Name, subject and folder are required.");
                var id = await _repository.AddSourceAsync(name, path, subject, courseLevel, exclusions);
                return new { id };
            }

            case "discoverSource":
            {
                var id = request.Payload.GetProperty("sourceId").GetInt64();
                var source = await _repository.GetSourceAsync(id) ?? throw new InvalidOperationException("Library source not found.");
                if (!source.LifecycleStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Restore/enable this library before discovery.");
                var protectedPaths = await _repository.GetDiscoveryProtectedPathsAsync(id);
                var scan = await _discovery.ScanAsync(source, protectedPaths);
                if (scan.Errors.Count > 0 && scan.Files.Count == 0)
                    throw new InvalidOperationException(scan.Errors[0]);
                var result = await _repository.SaveDiscoveryAsync(source, scan);
                await _repository.CleanLibraryIndexAsync(id);
                await _repository.RefreshResourceRelationshipsAsync(id);
                await _repository.RefreshLatestDiscoveryScanAuditAsync(id);
                return result;
            }

            case "getScannerSummary":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                return await _repository.GetScannerSummaryAsync(sourceId);
            }

            case "fingerprintScannerBatch":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                var limit = request.Payload.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(limitElement.GetInt32(), 1, 250)
                    : 100;
                var candidates = await _repository.GetFingerprintCandidatesAsync(sourceId, limit);
                var errors = new List<string>();
                var fingerprinted = 0;
                var skipped = 0;
                var failed = 0;
                foreach (var candidate in candidates)
                {
                    var fullPath = ResolveResourcePath(candidate.RelativePath, candidate.RootPath);
                    if (!File.Exists(fullPath))
                    {
                        skipped++;
                        await _repository.MarkFingerprintFailedAsync(candidate.ResourceId);
                        continue;
                    }

                    try
                    {
                        var fingerprint = await _fingerprintService.ComputeQuickFingerprintAsync(fullPath);
                        await _repository.SaveQuickFingerprintAsync(candidate.ResourceId, fingerprint);
                        fingerprinted++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failed++;
                        await _repository.MarkFingerprintFailedAsync(candidate.ResourceId);
                        if (errors.Count < 12) errors.Add($"{candidate.RelativePath}: {ex.Message}");
                    }
                }

                var summary = await _repository.GetScannerSummaryAsync(sourceId);
                return new FingerprintBatchResult(candidates.Count, candidates.Count, fingerprinted, skipped, failed, summary.DuplicateFingerprintGroups, summary.DuplicateFingerprintResources, errors);
            }

            case "analyseScannerBatch":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                var limit = request.Payload.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(limitElement.GetInt32(), 1, 100)
                    : 25;
                var ids = await _repository.GetStagedAnalysisCandidateIdsAsync(sourceId, limit);
                var result = await PrepareSelectedAsync(ids, "Staged scanner: new/changed structured content");
                var remaining = await _repository.GetStagedAnalysisRemainingAsync(sourceId);
                return new StagedAnalysisBatchResult(result.Requested, result.Analysed, result.ReusedAnalysis, result.Limited, result.Failed, remaining, result.Items.Select(x => x.ResourceId).ToArray());
            }

            case "getLatestContentPlacementRun":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                return await _repository.GetLatestContentPlacementRunAsync(sourceId);
            }

            case "runContentPlacementBatch":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                var focus = request.Payload.TryGetProperty("focus", out var focusElement) ? focusElement.GetString() ?? string.Empty : string.Empty;
                var limit = request.Payload.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(limitElement.GetInt32(), 1, 100)
                    : 25;
                return await RunContentPlacementAsync(sourceId, focus, limit);
            }

            case "cleanLibraryIndex":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                var result = await _repository.CleanLibraryIndexAsync(sourceId);
                await _repository.RefreshResourceRelationshipsAsync(sourceId);
                return result;
            }

            case "getDiscoveryReviewItems":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                var state = request.Payload.TryGetProperty("state", out var stateElement) ? stateElement.GetString() ?? "Pending" : "Pending";
                return await _repository.GetDiscoveryReviewItemsAsync(sourceId, state);
            }

            case "getDiscoveryReviewSummary":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                return await _repository.GetDiscoveryReviewSummaryAsync(sourceId);
            }

            case "applyDiscoveryOverride":
            {
                var p = request.Payload;
                var sourceId = p.GetProperty("sourceId").GetInt64();
                var action = p.GetProperty("action").GetString() ?? "Exclude";
                var paths = p.GetProperty("relativePaths").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
                var result = await _repository.ApplyDiscoveryOverrideAsync(sourceId, paths, action);
                await _repository.RefreshResourceRelationshipsAsync(sourceId);
                return result;
            }

            case "purgeDiscoveryPaths":
            {
                var p = request.Payload;
                var sourceId = p.GetProperty("sourceId").GetInt64();
                var paths = p.GetProperty("relativePaths").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
                return await _repository.PurgeDiscoveryPathsAsync(sourceId, paths);
            }

            case "addLearningCollectionCandidate":
            {
                var p = request.Payload;
                var sourceId = p.GetProperty("sourceId").GetInt64();
                var rootRelativePath = p.GetProperty("rootRelativePath").GetString() ?? "";
                var displayName = p.TryGetProperty("displayName", out var nameElement) ? nameElement.GetString() : null;
                return await _repository.AddLearningCollectionCandidateAsync(sourceId, rootRelativePath, displayName);
            }

            case "getLearningCollectionCandidates":
            {
                long? sourceId = request.Payload.TryGetProperty("sourceId", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Number
                    ? sourceElement.GetInt64()
                    : null;
                return await _repository.GetLearningCollectionCandidatesAsync(sourceId);
            }

            case "getResources":
            {
                var search = request.Payload.TryGetProperty("search", out var s) ? s.GetString() : null;
                var kind = request.Payload.TryGetProperty("mediaKind", out var k) ? k.GetString() : null;
                var status = request.Payload.TryGetProperty("studyStatus", out var st) ? st.GetString() : null;
                var libraryKey = request.Payload.TryGetProperty("libraryKey", out var lk) ? lk.GetString() : null;
                return await _repository.SearchResourcesAsync(search, kind, status, libraryKey);
            }

            case "getWorkspaceOverview":
            {
                var level = request.Payload.TryGetProperty("courseLevel", out var levelEl) ? levelEl.GetString() ?? "ONC" : "ONC";
                return await _repository.GetWorkspaceOverviewAsync(level);
            }

            case "addCurriculumSubject":
            {
                var p = request.Payload;
                return await _repository.AddCurriculumSubjectAsync(p.GetProperty("courseId").GetInt64(), p.GetProperty("name").GetString() ?? "");
            }

            case "getResourceOrganisation":
            {
                var id = request.Payload.GetProperty("resourceId").GetInt64();
                return await _repository.GetResourceOrganisationAsync(id)
                    ?? throw new InvalidOperationException("Resource not found.");
            }

            case "saveResourceOrganisation":
            {
                var payload = request.Payload;
                var id = payload.GetProperty("resourceId").GetInt64();
                var learningSubject = payload.TryGetProperty("learningSubject", out var ls) ? ls.GetString() ?? "" : "";
                var topic = payload.TryGetProperty("topic", out var t) ? t.GetString() ?? "" : "";
                var subtopic = payload.TryGetProperty("subtopic", out var sub) ? sub.GetString() ?? "" : "";
                var resourceType = payload.TryGetProperty("resourceType", out var rt) ? rt.GetString() ?? "Unknown" : "Unknown";
                var studyStatus = payload.TryGetProperty("studyStatus", out var ss) ? ss.GetString() ?? "Unsorted" : "Unsorted";
                var notes = payload.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
                return await _repository.SaveResourceOrganisationAsync(id, learningSubject, topic, subtopic, resourceType, studyStatus, notes);
            }

            case "getResourceAnalysis":
            {
                var id = request.Payload.GetProperty("resourceId").GetInt64();
                return await _repository.GetResourceAnalysisAsync(id);
            }

            case "scanResource":
            {
                var id = request.Payload.GetProperty("resourceId").GetInt64();
                var found = await _repository.GetResourceAsync(id) ?? throw new InvalidOperationException("Resource not found.");
                var fullPath = ResolveResourcePath(found.Resource.RelativePath, found.RootPath);
                if (!File.Exists(fullPath)) throw new FileNotFoundException("Resource is currently unavailable.", fullPath);

                ResourceAnalysis analysis;
                if (found.Resource.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    analysis = await _pdfAnalyser.AnalyseAsync(found.Resource, fullPath);
                else if (_structuredAnalyser.CanAnalyse(found.Resource.Extension))
                    analysis = await _structuredAnalyser.AnalyseAsync(found.Resource, fullPath, "Manual selected-resource scan");
                else
                    throw new InvalidOperationException("This resource type does not have a content scanner yet. It can still be organised and opened normally.");
                await _repository.SaveResourceAnalysisAsync(analysis);
                await _repository.SaveSourceAnalysisStagingAsync(analysis);
                return analysis;
            }

            case "prepareBulkResources":
            {
                var ids = ReadResourceIds(request.Payload);
                var purpose = request.Payload.TryGetProperty("purpose", out var pp) ? pp.GetString() ?? "Organise" : "Organise";
                return await PrepareSelectedAsync(ids, purpose);
            }

            case "applyBulkOrganisation":
            {
                var ids = ReadResourceIds(request.Payload);
                var prep = await PrepareSelectedAsync(ids, "Organise");
                var updated = 0;
                foreach (var item in prep.Items)
                {
                    await _repository.SaveResourceOrganisationAsync(item.ResourceId, item.SuggestedLearningSubject, item.SuggestedTopic,
                        item.SuggestedSubtopic, item.SuggestedResourceType, item.SuggestedStudyStatus, "Bulk-organised from selected-resource analysis.");
                    updated++;
                }
                return new BulkOrganisationResult(ids.Count, updated);
            }

            case "createBulkStudy":
            {
                var ids = ReadResourceIds(request.Payload);
                var prep = await PrepareSelectedAsync(ids, "CreateStudy");
                var created = 0;
                var reusedExact = 0;
                var linked = 0;
                var skipped = 0;
                var moduleIds = new List<long>();
                var skippedIneligible = prep.Items.Count(x => !x.EligibleForBulkStudy);
                foreach (var item in prep.Items)
                {
                    if (!item.EligibleForBulkStudy) continue;
                    await _repository.SaveResourceOrganisationAsync(item.ResourceId, item.SuggestedLearningSubject, item.SuggestedTopic,
                        item.SuggestedSubtopic, item.SuggestedResourceType, item.SuggestedStudyStatus, "Prepared for Study Builder from selected resources.");
                    var found = await _repository.GetResourceAsync(item.ResourceId);
                    if (found is null) continue;

                    if (item.ExistingModuleId is long existingId && item.ExistingSimilarity >= 0.999)
                    {
                        await _learningRepository.LinkResourceToModuleAsync(existingId, item.ResourceId, "Matched source");
                        moduleIds.Add(existingId);
                        reusedExact++;
                        linked++;
                        continue;
                    }
                    if (item.ExistingModuleId is not null && item.ExistingSimilarity >= 0.62)
                    {
                        skipped++;
                        continue;
                    }

                    var title = Path.GetFileNameWithoutExtension(found.Value.Resource.FileName);
                    var id = await _learningRepository.CreateOrReuseModuleFromResourceAsync(item.ResourceId, title,
                        "Draft created from selected ONC source material. Review and enrich before publishing.",
                        found.Value.Resource.Subject, found.Value.Resource.CourseLevel, item.SuggestedLearningSubject,
                        item.SuggestedTopic, item.SuggestedSubtopic, false);
                    moduleIds.Add(id);
                    created++;
                }
                return new BulkStudyResult(ids.Count, created, reusedExact, linked, skipped, skippedIneligible, moduleIds.Distinct().ToArray());
            }

            case "extractBulkLearning":
            {
                var ids = ReadResourceIds(request.Payload);
                var prep = await PrepareSelectedAsync(ids, "ExtractLearning");
                var processed = 0;
                var fresh = 0;
                var existing = 0;
                var possibleDuplicates = 0;
                var questions = 0;
                var formulas = 0;
                var lessons = 0;
                var other = 0;
                foreach (var item in prep.Items)
                {
                    var analysis = await _repository.GetResourceAnalysisAsync(item.ResourceId);
                    if (analysis is null || !analysis.HasSearchableText) continue;
                    var org = await _repository.SaveResourceOrganisationAsync(item.ResourceId, item.SuggestedLearningSubject, item.SuggestedTopic,
                        item.SuggestedSubtopic, item.SuggestedResourceType, item.SuggestedStudyStatus, "Learning candidates extracted for review.");
                    var found = await _repository.GetResourceAsync(item.ResourceId);
                    if (found is null) continue;
                    var result = await _learningRepository.ExtractLearningCandidatesAsync(found.Value.Resource, analysis, org);
                    processed += result.ResourcesProcessed;
                    fresh += result.NewCandidates;
                    existing += result.ExistingCandidatesLinked;
                    possibleDuplicates += result.PossibleDuplicatesForComparison;
                    questions += result.Questions;
                    formulas += result.Formulas;
                    lessons += result.LessonIdeas;
                    other += result.OtherCandidates;
                }
                return new LearningExtractionResult(processed, fresh, existing, possibleDuplicates, questions, formulas, lessons, other);
            }

            case "getDraftLearningCandidateCount":
                return new { count = await _learningRepository.GetDraftCandidateCountAsync("ONC") };

            case "getCandidateReviewSummary":
            {
                var level = request.Payload.TryGetProperty("courseLevel", out var crs) ? crs.GetString() ?? "ONC" : "ONC";
                return await _learningRepository.GetCandidateReviewSummaryAsync(level);
            }

            case "getLearningCandidates":
            {
                var level = request.Payload.TryGetProperty("courseLevel", out var cLevel) ? cLevel.GetString() ?? "ONC" : "ONC";
                var status = request.Payload.TryGetProperty("status", out var cStatus) ? cStatus.GetString() ?? "Draft" : "Draft";
                var type = request.Payload.TryGetProperty("candidateType", out var cType) ? cType.GetString() ?? "" : "";
                var learningSubject = request.Payload.TryGetProperty("learningSubject", out var cSubject) ? cSubject.GetString() ?? "" : "";
                var search = request.Payload.TryGetProperty("search", out var cSearch) ? cSearch.GetString() ?? "" : "";
                var limit = request.Payload.TryGetProperty("limit", out var cLimit) && cLimit.ValueKind == JsonValueKind.Number ? cLimit.GetInt32() : 250;
                return await _learningRepository.GetLearningCandidatesAsync(level, status, type, learningSubject, search, limit);
            }

            case "getLearningCandidate":
            {
                return await _learningRepository.GetLearningCandidateAsync(request.Payload.GetProperty("candidateId").GetInt64())
                    ?? throw new InvalidOperationException("Learning candidate not found.");
            }

            case "updateLearningCandidate":
            {
                var title = request.Payload.TryGetProperty("title", out var ct) ? ct.GetString() ?? "" : "";
                var body = request.Payload.TryGetProperty("body", out var cb) ? cb.GetString() ?? "" : "";
                var learningSubject = request.Payload.TryGetProperty("learningSubject", out var cls) ? cls.GetString() ?? "" : "";
                var topic = request.Payload.TryGetProperty("topic", out var ctp) ? ctp.GetString() ?? "" : "";
                var subtopic = request.Payload.TryGetProperty("subtopic", out var csp) ? csp.GetString() ?? "" : "";
                return await _learningRepository.UpdateLearningCandidateAsync(request.Payload.GetProperty("candidateId").GetInt64(), title, body, learningSubject, topic, subtopic);
            }

            case "setLearningCandidateStatus":
            {
                var status = request.Payload.GetProperty("status").GetString() ?? "Draft";
                return new { updated = await _learningRepository.SetLearningCandidateStatusAsync(ReadNumericIds(request.Payload, "candidateIds"), status) };
            }

            case "addCandidateToQuestionBank":
                return await _learningRepository.AddCandidateToQuestionBankAsync(request.Payload.GetProperty("candidateId").GetInt64());

            case "addCandidateToFormulaLibrary":
                return await _learningRepository.AddCandidateToFormulaLibraryAsync(request.Payload.GetProperty("candidateId").GetInt64());

            case "addCandidateToModule":
                return await _learningRepository.AddCandidateToModuleAsync(request.Payload.GetProperty("candidateId").GetInt64(), request.Payload.GetProperty("moduleId").GetInt64());

            case "createModuleFromCandidate":
                return await _learningRepository.CreateDraftModuleFromCandidateAsync(request.Payload.GetProperty("candidateId").GetInt64());

            case "getApprovedStudyProposal":
            {
                var level = request.Payload.TryGetProperty("courseLevel", out var proposalLevel) ? proposalLevel.GetString() ?? "ONC" : "ONC";
                return await _learningRepository.GetApprovedStudyProposalAsync(level);
            }

            case "buildApprovedStudy":
            {
                var level = request.Payload.TryGetProperty("courseLevel", out var buildLevel) ? buildLevel.GetString() ?? "ONC" : "ONC";
                var keys = request.Payload.TryGetProperty("groupKeys", out var groupKeys) && groupKeys.ValueKind == JsonValueKind.Array
                    ? groupKeys.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : [];
                return await _learningRepository.BuildApprovedStudyAsync(keys, level);
            }

            case "openResource":
            {
                var id = request.Payload.GetProperty("resourceId").GetInt64();
                var found = await _repository.GetResourceAsync(id) ?? throw new InvalidOperationException("Resource not found.");
                OpenResource(found.Resource, found.RootPath);
                return new { opened = true };
            }

            case "reconnectLibrary":
                return await _repository.ReconnectLibraryAsync(
                    request.Payload.GetProperty("sourceId").GetInt64(),
                    request.Payload.GetProperty("path").GetString() ?? "");
            case "setLibraryEnabled":
                return await _repository.SetLibraryEnabledAsync(request.Payload.GetProperty("sourceId").GetInt64(), request.Payload.GetProperty("enabled").GetBoolean());
            case "removeLibrary":
                return await _repository.RemoveLibraryAsync(request.Payload.GetProperty("sourceId").GetInt64());
            case "restoreLibrary":
                return await _repository.RestoreLibraryAsync(request.Payload.GetProperty("sourceId").GetInt64());
            case "purgeLibrary":
                return await _repository.PurgeLibraryAsync(request.Payload.GetProperty("sourceId").GetInt64());


            case "getStudyClassificationIssues":
                return await _learningRepository.GetStudyClassificationIssuesAsync();

            case "applyStudyClassificationRepairs":
                return await _learningRepository.ApplyStudyClassificationRepairsAsync(ReadNumericIds(request.Payload, "moduleIds"));

            case "getPendingBlockComparisons":
                return await _learningRepository.GetPendingBlockComparisonsAsync();

            case "resolveBlockComparison":
                return await _learningRepository.ResolveBlockComparisonAsync(
                    request.Payload.GetProperty("comparisonId").GetInt64(),
                    request.Payload.GetProperty("action").GetString() ?? "UseExisting");

            case "getLearningModules":
                return await _learningRepository.GetModulesAsync();

            case "createLearningModuleFromResource":
            {
                var payload = request.Payload;
                var resourceId = payload.GetProperty("resourceId").GetInt64();
                var title = payload.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                var summary = payload.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() : null;
                var id = await _learningRepository.CreateModuleFromResourceAsync(resourceId, title, summary);
                return await _learningRepository.GetModuleAsync(id);
            }

            case "getLearningModule":
            {
                var id = request.Payload.GetProperty("moduleId").GetInt64();
                return await _learningRepository.GetModuleAsync(id)
                    ?? throw new InvalidOperationException("Learning module not found.");
            }

            case "updateLearningModule":
            {
                var p = request.Payload;
                return await _learningRepository.UpdateModuleAsync(
                    p.GetProperty("moduleId").GetInt64(),
                    p.GetProperty("title").GetString() ?? "",
                    p.TryGetProperty("summary", out var sum) ? sum.GetString() ?? "" : "",
                    p.TryGetProperty("learningSubject", out var ls) ? ls.GetString() ?? "" : "",
                    p.TryGetProperty("topic", out var t) ? t.GetString() ?? "" : "",
                    p.TryGetProperty("subtopic", out var st) ? st.GetString() ?? "" : "",
                    p.TryGetProperty("status", out var status) ? status.GetString() ?? "Draft" : "Draft");
            }

            case "addLearningBlock":
            {
                var p = request.Payload;
                long? sourceResourceId = p.TryGetProperty("sourceResourceId", out var sr) && sr.ValueKind != JsonValueKind.Null ? sr.GetInt64() : null;
                int? sourcePage = p.TryGetProperty("sourcePageNumber", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetInt32() : null;
                int? sourceEndPage = p.TryGetProperty("sourceEndPageNumber", out var sep) && sep.ValueKind == JsonValueKind.Number ? sep.GetInt32() : null;
                int? sourceTimestamp = p.TryGetProperty("sourceTimestampSeconds", out var sts) && sts.ValueKind == JsonValueKind.Number ? sts.GetInt32() : null;
                double? tolerance = p.TryGetProperty("numericTolerance", out var tol) && tol.ValueKind == JsonValueKind.Number ? tol.GetDouble() : null;
                var draft = new LearningBlockDraft(
                    p.GetProperty("moduleId").GetInt64(),
                    p.GetProperty("blockType").GetString() ?? "Explanation",
                    p.TryGetProperty("title", out var bt) ? bt.GetString() ?? "" : "",
                    p.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                    p.TryGetProperty("formula", out var formula) ? formula.GetString() ?? "" : "",
                    p.TryGetProperty("prompt", out var prompt) ? prompt.GetString() ?? "" : "",
                    p.TryGetProperty("answerKind", out var ak) ? ak.GetString() ?? "None" : "None",
                    p.TryGetProperty("expectedAnswer", out var ea) ? ea.GetString() ?? "" : "",
                    tolerance,
                    p.TryGetProperty("hint", out var h) ? h.GetString() ?? "" : "",
                    p.TryGetProperty("explanation", out var ex) ? ex.GetString() ?? "" : "",
                    sourceResourceId,
                    sourcePage,
                    sourceEndPage,
                    sourceTimestamp,
                    p.TryGetProperty("sourceNote", out var sn) ? sn.GetString() ?? "" : "");
                return await _learningRepository.AddBlockAsync(draft);
            }

            case "updateLearningBlock":
            {
                var p = request.Payload;
                double? tolerance = p.TryGetProperty("numericTolerance", out var tol) && tol.ValueKind == JsonValueKind.Number ? tol.GetDouble() : null;
                var draft = new LearningBlockDraft(
                    p.GetProperty("moduleId").GetInt64(),
                    p.GetProperty("blockType").GetString() ?? "Explanation",
                    p.TryGetProperty("title", out var bt) ? bt.GetString() ?? "" : "",
                    p.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                    p.TryGetProperty("formula", out var formula) ? formula.GetString() ?? "" : "",
                    p.TryGetProperty("prompt", out var prompt) ? prompt.GetString() ?? "" : "",
                    p.TryGetProperty("answerKind", out var ak) ? ak.GetString() ?? "None" : "None",
                    p.TryGetProperty("expectedAnswer", out var ea) ? ea.GetString() ?? "" : "",
                    tolerance,
                    p.TryGetProperty("hint", out var h) ? h.GetString() ?? "" : "",
                    p.TryGetProperty("explanation", out var ex) ? ex.GetString() ?? "" : "",
                    null, null, null, null, "");
                return await _learningRepository.UpdateBlockAsync(p.GetProperty("blockId").GetInt64(), draft);
            }

            case "deleteLearningBlock":
            {
                var id = request.Payload.GetProperty("blockId").GetInt64();
                await _learningRepository.DeleteBlockAsync(id);
                return new { deleted = true };
            }

            case "checkLearningAnswer":
            {
                var blockId = request.Payload.GetProperty("blockId").GetInt64();
                var learnerAnswer = request.Payload.TryGetProperty("answer", out var ans) ? ans.GetString() ?? "" : "";
                var block = await _learningRepository.GetBlockAsync(blockId)
                    ?? throw new InvalidOperationException("Learning activity not found.");
                return _learningEvaluator.Evaluate(block, learnerAnswer);
            }

            case "openLearningSource":
            {
                var p = request.Payload;
                var resourceId = p.GetProperty("resourceId").GetInt64();
                int? page = p.TryGetProperty("pageNumber", out var pg) && pg.ValueKind == JsonValueKind.Number ? pg.GetInt32() : null;
                int? timestamp = p.TryGetProperty("timestampSeconds", out var ts) && ts.ValueKind == JsonValueKind.Number ? ts.GetInt32() : null;
                var found = await _repository.GetResourceAsync(resourceId) ?? throw new InvalidOperationException("Source resource not found.");
                OpenResource(found.Resource, found.RootPath, page, timestamp);
                return new { opened = true };
            }

            case "getUpdateStatus":
                return _updateService.GetStatus(CurrentVersion());

            case "checkForUpdates":
                return new
                {
                    currentVersion = CurrentVersion(),
                    updateAvailable = false,
                    feedConfigured = false,
                    message = "Online update checking is not configured yet. You can install a validated WICareerBuilder update package from Update Centre without using Bash."
                };

            case "pickUpdatePackage":
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Choose a WICareerBuilder update package",
                    Filter = "WICareerBuilder update (*.zip)|*.zip",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dialog.ShowDialog(_owner) != true) return new { staged = false, cancelled = true };
                return await _updateService.StagePackageAsync(dialog.FileName, CurrentVersion());
            }

            case "applyStagedUpdate":
            {
                var script = request.Payload.GetProperty("scriptPath").GetString() ?? "";
                _updateService.ApplyStagedUpdate(script);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(650);
                    _owner.Dispatcher.Invoke(() => Application.Current.Shutdown());
                });
                return new { applying = true, message = "WICareerBuilder will close and apply the staged update." };
            }

            case "getAppInfo":
                return new { version = CurrentVersion(), dataPath = AppPaths.Root, installed = AppPaths.IsInstalledBuild, demoMode = AppPaths.DemoMode };

            default:
                throw new InvalidOperationException($"Unknown request: {request.Type}");
        }
    }

    private async Task<BulkPreparationResult> PrepareSelectedAsync(IReadOnlyList<long> ids, string purpose)
    {
        if (ids.Count == 0) return new BulkPreparationResult(purpose, 0, 0, 0, 0, 0, []);
        if (ids.Count > 250) throw new InvalidOperationException("Process up to 250 selected resources at a time. This keeps deliberate analysis responsive and controlled.");
        var items = new List<BulkPreparationItem>();
        var analysed = 0;
        var reused = 0;
        var limited = 0;
        var failed = 0;

        foreach (var id in ids.Distinct())
        {
            var found = await _repository.GetResourceAsync(id);
            if (found is null) continue;
            var resolved = found.Value;
            var resource = resolved.Resource;
            ResourceAnalysis analysis;
            var reusedThis = false;
            var existing = await _repository.GetResourceAnalysisAsync(resource.Id);
            var structuredVersionMatches = existing is null
                || resource.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                || !_structuredAnalyser.CanAnalyse(resource.Extension)
                || existing.ScannerVersion.Equals(StructuredResourceAnalyser.ScannerVersion, StringComparison.OrdinalIgnoreCase);
            if (existing is not null && structuredVersionMatches && await _repository.IsResourceAnalysisFreshAsync(resource))
            {
                analysis = existing;
                reused++;
                reusedThis = true;
            }
            else if (resource.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Bulk workflows keep PDFs/books reference-first. A previously scanned PDF can contribute,
                // but an unscanned PDF is not silently read just because it was part of a bulk selection.
                analysis = existing ?? _structuredAnalyser.AnalyseMetadata(resource, "PDF reference-first metadata classification");
                limited++;
            }
            else if (_structuredAnalyser.CanAnalyse(resource.Extension) && resource.IsAvailable)
            {
                var fullPath = ResolveResourcePath(resource.RelativePath, resolved.RootPath);
                if (File.Exists(fullPath))
                {
                    analysis = await _structuredAnalyser.AnalyseAsync(resource, fullPath, purpose);
                    await _repository.SaveResourceAnalysisAsync(analysis);
                    await _repository.SaveSourceAnalysisStagingAsync(analysis);
                    if (analysis.AnalysisStatus == "Failed") failed++;
                    else if (analysis.AnalysisStatus == "Limited") limited++;
                    else analysed++;
                }
                else
                {
                    analysis = _structuredAnalyser.AnalyseMetadata(resource, "Unavailable-resource metadata classification");
                    limited++;
                }
            }
            else
            {
                analysis = existing ?? _structuredAnalyser.AnalyseMetadata(resource);
                limited++;
            }

            var learningSubject = LearningSubjectMapper.Map(analysis.SuggestedTopic);
            var topic = analysis.SuggestedSubtopics.FirstOrDefault() ?? "";
            var subtopic = analysis.SuggestedSubtopics.Skip(1).FirstOrDefault() ?? "";
            var type = analysis.SuggestedResourceType;
            var status = SuggestedStudyStatus(resource, type);
            var eligible = IsBulkStudyEligible(resource, type);
            var title = analysis.DocumentTitle ?? Path.GetFileNameWithoutExtension(resource.FileName);
            var match = await _learningRepository.FindSimilarModuleAsync(title, resource.CourseLevel, learningSubject, topic);
            var recommendation = Recommendation(resource, type, eligible, match.Similarity);

            items.Add(new BulkPreparationItem(resource.Id, resource.FileName, resource.MediaKind, resource.Extension,
                reusedThis ? "Cached analysis" : analysis.AnalysisStatus,
                type, learningSubject, topic, subtopic, status, recommendation,
                match.ModuleId, match.Title, match.Similarity, eligible,
                resource.MediaKind.Equals("Archive", StringComparison.OrdinalIgnoreCase), resource.RelatedCopyCount));
        }
        return new BulkPreparationResult(purpose, ids.Count, analysed, reused, limited, failed, items);
    }


    private static string SuggestedStudyStatus(ResourceItem resource, string type)
    {
        if (resource.MediaKind.Equals("Image", StringComparison.OrdinalIgnoreCase)) return "Reference";
        if (type is "Old Assignment" or "Datasheet" or "Manual / Technical Reference" or "Reference Document" or "Reference Material" or "Spreadsheet Reference") return "Reference";
        if (type is "Worksheet / Exercises" or "Teaching Presentation" or "Course Notes / Exercises" or "Revision Material" or "Interactive / Calculation Sheet") return "Useful";
        return "Study later";
    }

    private static bool IsBulkStudyEligible(ResourceItem resource, string type)
    {
        if (resource.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        if (!resource.MediaKind.Equals("Document", StringComparison.OrdinalIgnoreCase)
            && !resource.MediaKind.Equals("Presentation", StringComparison.OrdinalIgnoreCase)
            && !resource.MediaKind.Equals("Spreadsheet", StringComparison.OrdinalIgnoreCase)) return false;
        return type != "Old Assignment"
            && type != "Datasheet"
            && type != "Manual / Technical Reference"
            && type != "Reference Document"
            && type != "Reference Material"
            && type != "Spreadsheet Reference";
    }

    private static string Recommendation(ResourceItem resource, string type, bool eligible, double similarity)
    {
        if (resource.MediaKind.Equals("Image", StringComparison.OrdinalIgnoreCase)) return "Image Library: organise by Engineering subject/topic; do not turn the image itself into a lesson.";
        if (resource.MediaKind.Equals("Archive", StringComparison.OrdinalIgnoreCase)) return resource.HasArchiveCompanion ? "Archive companion of an included resource; keep linked rather than as a duplicate Library item." : "Archive awaiting inspection; contents are not automatically extracted.";
        if (type == "Old Assignment") return "Old Assignments / Reference: keep the source and use Extract Learning for useful questions, formulas or examples.";
        if (type is "Datasheet" or "Manual / Technical Reference") return "Datasheets & Manuals / Reference Library.";
        if (resource.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return "Reference-first PDF. Scan and create study deliberately when you choose this reading material.";
        if (similarity >= 0.62 && similarity < 1) return "Possible related prototype learning exists. Preserve it for comparison; v0.6 output creation happens after source reconstruction.";
        if (similarity >= 0.999) return "Matching prototype learning already exists. Preserve/link provenance rather than duplicate it.";
        return eligible ? "Potential lesson/revision source: organise now; analyse and reconstruct before any composed output is created." : "Organise/reference resource.";
    }

    private async Task<ContentPlacementBatchResult> RunContentPlacementAsync(long? sourceId, string focus, int limit)
    {
        focus = (focus ?? string.Empty).Trim();
        var candidates = await _repository.GetContentPlacementCandidatesAsync(sourceId, focus, limit);
        var runId = await _repository.StartContentPlacementRunAsync(sourceId, focus, candidates.Count);

        var items = new List<ContentPlacementItem>();
        var fingerprinted = 0;
        var analysed = 0;
        var cached = 0;
        var autoOrganised = 0;
        var partiallyOrganised = 0;
        var needsReview = 0;
        var protectedExisting = 0;
        var limited = 0;
        var failed = 0;

        foreach (var candidate in candidates)
        {
            var found = await _repository.GetResourceAsync(candidate.ResourceId);
            if (found is null) continue;
            var resource = found.Value.Resource;
            var fullPath = ResolveResourcePath(candidate.RelativePath, candidate.RootPath);
            var fingerprintedThisRun = false;

            if (!File.Exists(fullPath))
            {
                await _repository.MarkFingerprintFailedAsync(candidate.ResourceId);
                failed++;
                var missing = new ContentPlacementItem(candidate.ResourceId, candidate.FileName, candidate.RelativePath, candidate.MediaKind,
                    "", "", "", resource.ResourceType, 0, 0, 0, 0,
                    "Failed", "File is unavailable; placement was not attempted.", false, "Failed");
                items.Add(missing);
                await _repository.SaveContentPlacementItemAsync(runId, missing);
                continue;
            }

            if (!candidate.FingerprintStatus.Equals("Complete", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var fingerprint = await _fingerprintService.ComputeQuickFingerprintAsync(fullPath);
                    await _repository.SaveQuickFingerprintAsync(candidate.ResourceId, fingerprint);
                    fingerprinted++;
                    fingerprintedThisRun = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await _repository.MarkFingerprintFailedAsync(candidate.ResourceId);
                    failed++;
                    var fingerprintFailure = new ContentPlacementItem(candidate.ResourceId, candidate.FileName, candidate.RelativePath, candidate.MediaKind,
                        "", "", "", resource.ResourceType, 0, 0, 0, 0,
                        "Failed", "Fingerprint failed: " + ex.Message, false, "Failed");
                    items.Add(fingerprintFailure);
                    await _repository.SaveContentPlacementItemAsync(runId, fingerprintFailure);
                    continue;
                }
            }

            ResourceAnalysis analysis;
            var existingAnalysis = await _repository.GetResourceAnalysisAsync(resource.Id);
            var analysisFresh = existingAnalysis is not null
                && existingAnalysis.ScannerVersion.Equals(StructuredResourceAnalyser.ScannerVersion, StringComparison.OrdinalIgnoreCase)
                && await _repository.IsResourceAnalysisFreshAsync(resource);

            if (analysisFresh)
            {
                analysis = existingAnalysis!;
                cached++;
            }
            else
            {
                analysis = await _structuredAnalyser.AnalyseAsync(resource, fullPath, focus.Length > 0
                    ? $"Focused content placement: {focus}"
                    : "Staged content placement");
                await _repository.SaveResourceAnalysisAsync(analysis);
                await _repository.SaveSourceAnalysisStagingAsync(analysis);
                if (analysis.AnalysisStatus == "Failed") failed++;
                else if (analysis.AnalysisStatus == "Limited") limited++;
                else analysed++;
            }

            if (analysis.AnalysisStatus == "Failed")
            {
                var failedItem = new ContentPlacementItem(resource.Id, resource.FileName, resource.RelativePath, resource.MediaKind,
                    "", "", "", analysis.SuggestedResourceType, 0, 0, 0, 0,
                    "Failed", analysis.ErrorMessage ?? "Content analysis failed.",
                    fingerprintedThisRun, analysis.AnalysisStatus);
                items.Add(failedItem);
                await _repository.SaveContentPlacementItemAsync(runId, failedItem);
                continue;
            }

            var learningSubject = LearningSubjectMapper.Map(analysis.SuggestedTopic);
            var topic = analysis.SuggestedSubtopics.FirstOrDefault() ?? string.Empty;
            var subtopic = analysis.SuggestedSubtopics.Skip(1).FirstOrDefault() ?? string.Empty;
            var confidence = PlacementDecisionPolicy.Assess(resource, analysis, learningSubject, topic, subtopic);
            var placementDecision = PlacementDecisionPolicy.Decide(learningSubject, topic, subtopic, confidence);

            // v0.9.2 applies only the deepest hierarchy level that independently meets the threshold.
            var appliedSubject = placementDecision.AppliedSubject;
            var appliedTopic = placementDecision.AppliedTopic;
            var appliedSubtopic = placementDecision.AppliedSubtopic;
            var isPartial = placementDecision.IsPartial;

            var current = await _repository.GetResourceOrganisationAsync(resource.Id);
            var provenance = await _repository.GetResourceOrganisationProvenanceAsync(resource.Id);
            var applicationDecision = PlacementApplicationPolicy.Decide(placementDecision, current, provenance.Source);
            var priorContentPlacement = applicationDecision.PriorContentPlacement;
            var sameAppliedPlacement = applicationDecision.SameAppliedPlacement;

            string disposition = applicationDecision.Disposition;
            string proposalStatus = applicationDecision.ProposalStatus;
            string reason;

            switch (applicationDecision.Action)
            {
                case PlacementApplicationAction.NeedsReview:
                    if (applicationDecision.WithdrawPriorAutomatic)
                        await _repository.ClearPriorContentAnalysisOrganisationAsync(resource.Id);
                    reason = (priorContentPlacement ? "The prior automatic ContentAnalysis placement was withdrawn for review. " : string.Empty)
                        + "The subject/family hierarchy did not independently reach the automatic-placement threshold. " + confidence.Reason;
                    needsReview++;
                    break;

                case PlacementApplicationAction.AlreadyPlaced:
                    reason = isPartial
                        ? "Existing protected organisation already matches the confidently supported hierarchy; the proposed deeper subtopic remains review-only. " + confidence.Reason
                        : "Existing virtual organisation already matches the content-derived placement. " + confidence.Reason;
                    if (isPartial) needsReview++;
                    break;

                case PlacementApplicationAction.ProtectedExisting:
                    reason = "A manual/legacy organisation is present, so CareerBuilder did not overwrite it automatically. " + confidence.Reason;
                    protectedExisting++;
                    break;

                case PlacementApplicationAction.ApplyPartial:
                {
                    var status = SuggestedStudyStatus(resource, analysis.SuggestedResourceType);
                    await _repository.SaveResourceOrganisationAsync(
                        resource.Id, appliedSubject, appliedTopic, "", analysis.SuggestedResourceType, status,
                        $"Auto-organised to family level from extracted content by v0.9.2. Subject {confidence.Subject:0.00}; topic {confidence.Topic:0.00}; proposed subtopic {subtopic} at {confidence.Subtopic:0.00} remains review-only. Original file unchanged.",
                        default, "ContentAnalysis:0.9.2", Math.Min(confidence.Subject, confidence.Topic));
                    reason = priorContentPlacement
                        ? "A prior automatic placement was safely re-evaluated. The subject/family placement is confident, but the deeper subtopic was withdrawn and left for review. " + confidence.Reason
                        : "Subject/family placement was applied, but the deeper subtopic remains review-only because its own confidence is below threshold. " + confidence.Reason;
                    partiallyOrganised++;
                    needsReview++;
                    break;
                }

                case PlacementApplicationAction.ApplyFull:
                {
                    var status = SuggestedStudyStatus(resource, analysis.SuggestedResourceType);
                    reason = priorContentPlacement && !sameAppliedPlacement
                        ? "A prior automatic ContentAnalysis placement was safely re-evaluated and replaced. " + confidence.Reason
                        : "All applied hierarchy levels independently passed the automatic-placement threshold. " + confidence.Reason;
                    await _repository.SaveResourceOrganisationAsync(
                        resource.Id, appliedSubject, appliedTopic, appliedSubtopic, analysis.SuggestedResourceType, status,
                        $"Auto-organised from extracted content by v0.9.2. Subject {confidence.Subject:0.00}; topic {confidence.Topic:0.00}; subtopic {confidence.Subtopic:0.00}. Original file unchanged.",
                        default, "ContentAnalysis:0.9.2", confidence.Overall);
                    if (!sameAppliedPlacement) autoOrganised++;
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unsupported placement application action: {applicationDecision.Action}");
            }

            var fingerprintValue = await _repository.GetQuickFingerprintAsync(resource.Id);
            await _repository.SaveClassificationProposalAsync(
                resource.Id, learningSubject, topic, subtopic, analysis.SuggestedResourceType,
                confidence.Overall, confidence.Subject, confidence.Topic, confidence.Subtopic,
                proposalStatus, reason, StructuredResourceAnalyser.ScannerVersion, fingerprintValue);

            var item = new ContentPlacementItem(
                resource.Id, resource.FileName, resource.RelativePath, resource.MediaKind,
                learningSubject, topic, subtopic, analysis.SuggestedResourceType,
                confidence.Overall, confidence.Subject, confidence.Topic, confidence.Subtopic,
                disposition, reason, fingerprintedThisRun, analysis.AnalysisStatus);
            items.Add(item);
            await _repository.SaveContentPlacementItemAsync(runId, item);
        }

        var remaining = await _repository.GetContentPlacementRemainingAsync(sourceId, focus);
        await _repository.CompleteContentPlacementRunAsync(runId, items.Count, fingerprinted, analysed, cached, autoOrganised, partiallyOrganised, needsReview, protectedExisting, limited, failed);
        return new ContentPlacementBatchResult(
            runId, focus, candidates.Count, items.Count, fingerprinted, analysed, cached, autoOrganised, partiallyOrganised,
            needsReview, protectedExisting, limited, failed, remaining, items, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<long> ReadResourceIds(JsonElement payload)
        => payload.TryGetProperty("resourceIds", out var ids)
            ? ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt64()).Distinct().ToArray()
            : [];

    private static IReadOnlyList<long> ReadNumericIds(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt64()).Distinct().ToArray()
            : [];

    private static string ResolveResourcePath(string relativePath, string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resource path is outside its library source.");
        return fullPath;
    }

    private void OpenResource(ResourceItem resource, string rootPath, int? pageNumber = null, int? timestampSeconds = null)
    {
        var fullPath = ResolveResourcePath(resource.RelativePath, rootPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Resource is currently unavailable.", fullPath);

        if (resource.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            new PdfViewerWindow(fullPath, pageNumber) { Owner = _owner }.Show();
            return;
        }
        if (resource.MediaKind.Equals("Video", StringComparison.OrdinalIgnoreCase))
        {
            new VideoPlayerWindow(fullPath, timestampSeconds) { Owner = _owner }.Show();
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
    }

    private static string CurrentVersion() => typeof(WebBridge).Assembly.GetName().Version?.ToString(3) ?? "0.9.2";

    private async Task ReplyAsync(BridgeResponse response)
    {
        var json = JsonSerializer.Serialize(response, _json);
        await _webView.ExecuteScriptAsync($"window.WICareer && window.WICareer.receive({json});");
    }
}
