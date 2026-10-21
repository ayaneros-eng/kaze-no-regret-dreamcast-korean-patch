# 외부 구성요소와 라이선스

## chdman 0.251

CHD 추출·재압축·검증에 사용하며 별도 프로세스로 실행합니다. 동봉한 파일은 공식 MAME 0.251 Windows 배포본의 chdman.exe와 바이트 단위로 같습니다. 공식 압축본도 공식 SHA256SUMS와 대조했습니다.

- 실행 파일 SHA-256: `972cad9fc243ba8d393cd9bfe566af0740e9df543b628b076b99fe77b8d24d20`
- 공식 바이너리: https://github.com/mamedev/mame/releases/download/mame0251/mame0251b_64bit.exe
- 바이너리/소스 체크섬: https://github.com/mamedev/mame/releases/download/mame0251/SHA256SUMS
- 정확한 버전의 전체 소스: https://github.com/mamedev/mame/releases/download/mame0251/mame0251s.exe
- 빌드 규칙: https://github.com/mamedev/mame/blob/mame0251/scripts/src/tools.lua

- MAME 0.251: https://github.com/mamedev/mame/tree/mame0251
- 해당 버전 전체 소스: https://github.com/mamedev/mame/archive/refs/tags/mame0251.zip
- chdman 소스: https://github.com/mamedev/mame/blob/mame0251/src/tools/chdman.cpp
- 포함된 라이선스·저작권 고지: `COPYING-MAME.txt`

chdman 프런트엔드는 Aaron Giles의 BSD-3-Clause 코드입니다. MAME 전체의 대표 라이선스만으로 도구의 모든 파일을 단정하지 않습니다. util/osd 코드와 Expat, 7-Zip/LZMA, FLAC, utf8proc, zlib 등은 각각의 저작권·라이선스를 유지합니다. 공식 COPYING, docs/legal 전문, 관련 라이브러리의 라이선스·저작자 고지와 소스 헤더는 LICENSES.txt에 모았습니다. 고지 원본의 경로·해시는 license-provenance.json에 기록했습니다. 이 목록은 관련 소스 트리를 넓게 포함하며 최소 링크 구성만을 나타내는 확정 SBOM은 아닙니다.

## Noto Sans KR

한글 자막 글리프에는 Noto Sans KR을 사용했습니다. 변환한 데이터의 이름은 Kaze caption glyph subset입니다. 원 TTF/OTF는 포함하지 않으며, 글리프 서브셋도 OFL과 저작권 고지를 유지합니다.

- 저작권: Copyright 2014-2021 Adobe, Reserved Font Name 'Source'

- 원 글꼴 프로젝트: https://github.com/notofonts/noto-cjk
- 라이선스: SIL Open Font License 1.1, `OFL-NotoSansKR.txt`

## 게임 데이터

게임 사진·음성·일본어 원본 프로그램의 권리는 원 권리자에게 있습니다. 직접 작성한 도구·렌더러·스크립트의 MIT 라이선스 범위는 LICENSE에 있습니다. 게임과 게임에서 파생한 번역·수정 데이터에는 이 허락이 적용되지 않습니다. 무료 비공식 팬 번역, 원본 필요 조건과 잔여 권리 문제는 RIGHTS.md를 확인하세요.

적용기와 편집기 모두 라이선스 전문을 실행 파일 안에 포함하고 [라이선스·권리 고지] 또는 [라이선스] 버튼으로 표시합니다. Windows 배포 ZIP에도 전문을 별도 파일로 동봉합니다.
