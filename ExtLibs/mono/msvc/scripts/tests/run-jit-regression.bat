@ECHO OFF
SETLOCAL
SETLOCAL ENABLEDELAYEDEXPANSION

SET TEMP_PATH=%PATH%
SET MONO_RESULT=1

CALL %~dp0setup-env.bat
IF NOT ERRORLEVEL == 0 (
	ECHO Failed to setup mono paths.
	GOTO ON_ERROR
)

CALL %~dp0setup-toolchain.bat
IF NOT ERRORLEVEL == 0 (
	ECHO Failed to setup toolchain.
	GOTO ON_ERROR
)

IF NOT EXIST "%MONO_BCL_PATH%" (
	ECHO Could not find "%MONO_BCL_PATH%".
	GOTO ON_ERROR
)

SET MONO_RUNTIME_TEST_PATH=%MONO_MINI_HOME%
SET MONO_TEST_PATH=%MONO_TEST_BUILD_DIR%

SET MONO_PATH=%MONO_BCL_PATH%;%MONO_TEST_PATH%;%MONO_RUNTIME_TEST_PATH%

SET MONO_RUNTIME_REGRESSION_TESTS=basic.exe ^
basic-float.exe ^
basic-long.exe ^
basic-calls.exe ^
objects.exe ^
arrays.exe ^
basic-math.exe ^
exceptions.exe ^
iltests.exe ^
devirtualization.exe ^
generics.exe ^
basic-simd.exe ^
basic-vectors.exe ^
ratests.exe ^
unaligned.exe ^
builtin-types.exe

SET RUN_TARGET=%1

IF /i "all" == "%RUN_TARGET%" (
	SET RUN_TARGET=
	FOR %%a IN (%MONO_RUNTIME_REGRESSION_TESTS%) DO (
		SET FOUND_TEST_TARGET=
		CALL :FIND_TEST FOUND_TEST_TARGET %%a
		IF NOT "!RUN_TARGET!" == "" (
			SET RUN_TARGET=!RUN_TARGET! !FOUND_TEST_TARGET!
		) ELSE (
			SET RUN_TARGET=!FOUND_TEST_TARGET!
		)
	)

) ELSE (
	SET FOUND_TEST_TARGET=
	CALL :FIND_TEST FOUND_TEST_TARGET %RUN_TARGET%
	SET RUN_TARGET=!FOUND_TEST_TARGET!
)

GOTO END_FIND_TEST

:FIND_TEST
FOR /d %%d IN (%MONO_TEST_PATH%\*) DO (
	IF EXIST %%d\%2 (
		SET %1=%%d\%2
		GOTO RETURN_FIND_TEST
	)
)

IF EXIST %MONO_RUNTIME_TEST_PATH%\%2 (
	SET %1=%MONO_RUNTIME_TEST_PATH%\%2
)

:RETURN_FIND_TEST
GOTO :EOF

:END_FIND_TEST

REM Debug output options.

REM SET MONO_LOG_LEVEL=debug
REM SET MONO_LOG_MASK=asm,aot

SET MONO_LOG_LEVEL=
SET MONO_LOG_MASK=


ECHO %MONO_JIT_EXECUTABLE% --regression %RUN_TARGET%.
%MONO_JIT_EXECUTABLE% --regression %RUN_TARGET%

IF NOT ERRORLEVEL == 0 (
	ECHO Failed JIT regression execute of %RUN_TARGET%.
	GOTO ON_ERROR
)

GOTO ON_EXIT

:ON_ERROR
	ECHO Failed JIT regression execute.
	SET MONO_RESULT=ERRORLEVEL
	GOTO ON_EXIT

:ON_EXIT
	SET PATH=%TEMP_PATH%
	EXIT /b %MONO_RESULT%

@ECHO ON
