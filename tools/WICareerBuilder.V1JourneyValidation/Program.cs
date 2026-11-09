using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;
using WICareerBuilder.Data.Database;
using WICareerBuilder.Data.Repositories;
using WICareerBuilder.Documents.Analysis;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var fixture = Path.Combine(root,"tests","Fixtures","V1Journey","DC Motor Speed Control.txt");
if(!File.Exists(fixture)){Console.Error.WriteLine($"Fixture missing: {fixture}");return 2;}

var info=new FileInfo(fixture);
var resource=new ResourceItem(1,1,"V1 Journey","Electrical Machines","","V1Journey/DC Motor Speed Control.txt",info.Name,info.Extension,info.Length,info.LastWriteTimeUtc,"Document","Unknown","",true,false,"","","","Unsorted");
var analyser=new StructuredResourceAnalyser();
var analysis=await analyser.AnalyseAsync(resource,fixture,"V1 scanner-to-career journey validation");
var subject=LearningSubjectMapper.Map(analysis.SuggestedTopic);
var topic=analysis.SuggestedSubtopics.FirstOrDefault()??"";
var subtopic=analysis.SuggestedSubtopics.Skip(1).FirstOrDefault()??"";
var confidence=PlacementDecisionPolicy.Assess(resource,analysis,subject,topic,subtopic);
var placement=PlacementDecisionPolicy.Decide(subject,topic,subtopic,confidence);
if(!placement.CanApplySubject||!placement.CanApplyTopic||!placement.CanApplySubtopic||!subject.Equals("Electrical Machines",StringComparison.OrdinalIgnoreCase)||!topic.Equals("DC Machines",StringComparison.OrdinalIgnoreCase)||!subtopic.Equals("Speed Control",StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"V1-JOURNEY-CLASSIFY FAIL subject={subject} topic={topic} subtopic={subtopic} confidence={confidence}");return 3;
}
Console.WriteLine($"V1-JOURNEY-CLASSIFY PASS {subject} -> {topic} -> {subtopic}");

var temp=Path.Combine(Path.GetTempPath(),"WICareerBuilder.V1Journey",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try
{
    var database=new DatabaseService(Path.Combine(temp,"careerbuilder.db"));await database.InitializeAsync();var repo=new LibraryRepository(database);
    var projectId=await repo.CreateCareerProjectAsync("V1 DC Motor Proof","Engineering","Headless scanner-to-career proof","Private",true);
    var skillId=await repo.CreateCareerSkillAsync("DC motor speed control","Electrical Engineering","Practical DC motor control");
    var evidenceId=await repo.CreateCareerEvidenceAsync("V1 scanned DC motor proof","ProjectTest","DemonstratedProject","Evidence produced from the validated fixture",projectId,null,true,true,"Reviewed","Private",true);
    await repo.LinkEvidenceSkillAsync(evidenceId,skillId,"Strong","Validated practical route");
    await repo.AddEvidenceProvenanceAsync(evidenceId,"ScannedFixture",fixture,subject,topic,subtopic,"V1 headless journey");

    var privateGate=await repo.AuthorizeExternalProcessingAsync("Evidence",evidenceId,"ExternalAI");
    if(privateGate.Allowed){Console.Error.WriteLine("V1-JOURNEY-PRIVACY FAIL private evidence crossed external gate");return 4;}
    Console.WriteLine("V1-JOURNEY-PRIVACY PASS private/no-web evidence denied external processing");

    var cvId=await repo.CreateCareerCvAsync("V1 Engineering CV","Engineering Technician","Engineering");
    var blocked=false;try{await repo.AddEvidenceToCvAsync(cvId,evidenceId,"Projects","Built and tested DC motor speed control.");}catch(InvalidOperationException){blocked=true;}
    if(!blocked){Console.Error.WriteLine("V1-JOURNEY-APPROVAL FAIL evidence entered CV before approval");return 5;}
    Console.WriteLine("V1-JOURNEY-APPROVAL-GATE PASS unapproved evidence rejected");

    await repo.SetEvidenceCvApprovalAsync(evidenceId,true);
    await repo.AddEvidenceToCvAsync(cvId,evidenceId,"Projects","Built and tested DC motor speed control.");
    var items=await repo.GetCareerCvItemsAsync(cvId);if(items.Count!=1){Console.Error.WriteLine("V1-JOURNEY-CV FAIL");return 6;}
    var provenance=await repo.GetEvidenceProvenanceAsync(evidenceId);if(provenance.Count!=1||provenance[0].Subtopic!="Speed Control"){Console.Error.WriteLine("V1-JOURNEY-PROVENANCE FAIL");return 7;}
    var portfolioId=await repo.CreateCareerPortfolioEntryAsync(projectId,"V1 DC Motor Portfolio","Control motor speed safely","Validated fixture and evidence route passed");
    var portfolio=(await repo.GetCareerPortfolioEntriesAsync()).Single(x=>x.Id==portfolioId);
    if(portfolio.UserApprovedExport||!portfolio.NoWebAccess){Console.Error.WriteLine("V1-JOURNEY-PORTFOLIO FAIL");return 8;}
    Console.WriteLine("V1-JOURNEY-CV PASS approved evidence entered one career-specific CV");
    Console.WriteLine("V1-JOURNEY-PROVENANCE PASS classification provenance retained");
    Console.WriteLine("V1-JOURNEY-PORTFOLIO PASS private draft created without export approval");
    Console.WriteLine("V1-JOURNEY-001 PASS");
    return 0;
}
finally{try{Directory.Delete(temp,true);}catch{}}
