@echo off
echo ===================================
echo Batch Test Script
echo ===================================
echo Arguments: %*
echo Current time: %TIME%
echo Current directory: %CD%
echo ===================================

REM Check if first argument is "fail"
if "%1"=="fail" (
  echo Test result: FAIL ^(intentional^)
  exit /b 1
)

echo Test result: PASS
exit /b 0
