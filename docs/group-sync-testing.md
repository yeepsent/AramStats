# 공동 통계 개발 환경 검증

앱은 Supabase Auth의 개별 계정과 `aramstats_*` RPC를 사용한다. 각 계정에는 한 그룹과 한 Riot PUUID를 등록한다. **공동 통계**에서 로그인하면 즉시 동기화하며, 앱을 열어 둔 동안 60초마다 다시 동기화한다. 등록된 Riot 계정을 앱에 연결하면 저장된 증바람(큐 2400) 상세 경기 중 자신이 참여한 10인 경기를 업로드한다. 다운로드와 통계 조회를 위해서는 Supabase 로그인만 필요하다. 세션과 공동 통계 캐시는 실행 중 메모리에 유지한다.

## 테스트용 Supabase 설정

1. 격리된 Supabase 프로젝트에 `supabase/migrations/202611220001_group_sync.sql`을 적용한다.
2. Auth Users에서 멤버별 이메일 계정을 만든다. 사용할 계정의 이메일을 확인 완료 처리하고, SQL Editor에서 각 Auth 사용자 UUID를 올바른 Riot PUUID와 연결한다.

   ```sql
   insert into public.aramstats_groups (name)
   values ('테스트 그룹')
   returning id;

   insert into public.aramstats_members (id, group_id, puuid, display_name)
   values ('<Auth 사용자 UUID>', '<그룹 UUID>', '<Riot PUUID>', '표시 이름');

   -- 기존 사용자의 그룹 접근을 철회한다.
   update public.aramstats_members
   set revoked_at = now()
   where id = '<Auth 사용자 UUID>';
   ```

   동일 멤버 행에서 연결된 PUUID와 그룹은 유지한다. Riot 계정이나 그룹을 교체할 때는 기존 멤버를 철회하고 새 Auth 계정으로 등록한다. 표시 이름은 기존 행에서 수정할 수 있다.

3. Windows PowerShell에서 프로젝트 URL과 공개 publishable key를 설정하고 같은 창에서 앱을 시작한다.

   ```powershell
   $env:ARAMSTATS_SUPABASE_URL = 'https://<project-ref>.supabase.co'
   $env:ARAMSTATS_SUPABASE_PUBLISHABLE_KEY = 'sb_publishable_...'
   dotnet run --project .\AramStats.csproj -c Release
   ```

4. PC A에서 공동 통계에 로그인하고 등록된 롤 계정으로 전적을 수집한다. PC B에서 같은 그룹의 다른 계정으로 로그인한 뒤 자동 동기화와 **지금 동기화**를 각각 확인한다. 승패, 챔피언별 KDA, 같은 팀 조합과 각 PC의 `나` 표기를 대조한다. 양쪽 PC에서 같은 경기 재전송, 네트워크 단절·복구, 멤버 철회도 확인한다.

기존 로컬 전적과 친구 목록은 그대로 사용한다. 공동 데이터는 별도 탭에서 조회하며 서버가 멤버로 등록된 플레이어의 통계 필드만 제공한다. 서버는 멤버가 제출한 경기의 형식과 그룹 권한을 확인한다. 라이엇 원본은 서버에서 검증하지 않으므로 화면에 **멤버 제출 전적**으로 표시한다. 새 테이블은 기존 개발용 Edge Function이나 테스트 행을 읽지 않는다. 기존 공유 토큰은 이전 수동 개발 테스트 전용이다.

## 로컬 검증

```sh
dotnet build -c Release -p:EnableWindowsTargeting=true
npm ci --prefix tests
npm test --prefix tests
```

SQL 테스트는 로컬 PostgreSQL에서 권한, 입력 검증, 중복과 충돌, 다운로드 필드, 그룹 간 격리, 페이지 처리 및 멤버 철회를 확인한다. 실제 Supabase Auth와 REST 게이트웨이 및 Windows에서의 롤 클라이언트 연동은 위 두 PC 검증에서 확인한다.
