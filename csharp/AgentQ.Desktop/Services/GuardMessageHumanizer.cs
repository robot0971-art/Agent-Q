namespace AgentQ.Desktop.Services;

public static class GuardMessageHumanizer
{
    public static string BuildTaskContractRejectedMessage(TaskContract contract)
    {
        var goal = string.IsNullOrWhiteSpace(contract.Goal)
            ? "요청한 작업"
            : contract.Goal.Trim();

        return contract.Intent switch
        {
            TaskContractIntent.CreateProject =>
                "프로젝트 생성이 아직 실제 파일 생성과 검증 증거로 확인되지 않았습니다. " +
                "Agent Q가 완료라고 말하지 않도록 중단했습니다. 승인된 생성 계획, 생성된 파일, 검증 결과를 다시 확인해야 합니다.",
            TaskContractIntent.ModifyCode =>
                "코드 수정이 아직 실제 파일 변경 증거로 확인되지 않았습니다. " +
                "Agent Q가 말로만 완료했다고 답하지 않도록 중단했습니다. 관련 파일을 다시 확인하고 실제 수정과 검증을 진행해야 합니다.",
            TaskContractIntent.RunVerification =>
                "빌드나 테스트 실행 결과가 아직 확인되지 않았습니다. " +
                "Agent Q가 실행하지 않은 검증을 완료처럼 보고하지 않도록 중단했습니다. 안전한 검증 명령을 실제로 실행한 뒤 결과를 보고해야 합니다.",
            TaskContractIntent.CreateDirectory =>
                "폴더 생성이 아직 실제 생성 증거로 확인되지 않았습니다. " +
                "Agent Q가 말로만 생성했다고 답하지 않도록 중단했습니다. 요청한 폴더 생성 도구를 다시 실행해야 합니다.",
            TaskContractIntent.CreateFile =>
                "파일 생성이 아직 실제 파일 변경 증거로 확인되지 않았습니다. " +
                "Agent Q가 말로만 생성했다고 답하지 않도록 중단했습니다. 요청한 파일을 실제로 생성해야 합니다.",
            _ =>
                $"요청한 작업이 아직 실행 증거로 확인되지 않았습니다. Agent Q가 완료라고 말하지 않도록 중단했습니다. 목표: {goal}"
        };
    }
}
