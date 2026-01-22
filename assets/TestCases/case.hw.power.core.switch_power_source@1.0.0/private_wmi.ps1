$ErrorActionPreference = "Stop"

function Wait-WmiReady {
    # if UUT just boot to OS, wmi may not ready and get error result
    # https://vcosmos.hpcloud.hp.com/tasks?id=63e5a7164f83bd0011f73bd9
    # ReturnCodeError: [1] Get-WmiObject : Out of memory
    
    for ($i = 0; $i -lt 120; $i++) {
        try {        
            Get-Date
            break        
        }        
        catch {        
            timeout 1
        }        
        if ($i -eq 119) {            
            [Console]::Error.WriteLine("fail to Get-Date")
            exit 1
        }
    }
    
    $limit = (Get-Date).AddSeconds(120)
    $done_flag = $false
    while ((Get-Date) -le $limit) {
        try {
            $null = Get-WmiObject "win32_bios"
            $done_flag = $true
            break
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
    if ($done_flag -eq $false) {
        $msg = "Fail to access wmi, {0}: {1}" -f $Error[0].CategoryInfo.Activity, $Error[0]
        [Console]::Error.WriteLine($msg)
        exit 1
    }
}

function Invoke-PrivateWmi($Command, $CommandType, $InData, $SizeOut) {
    Wait-WmiReady
    try {
        if ($Command -lt 0) {
            throw "arg command must be greater than or equal to 0."
        }
        elseif ($CommandType -lt 0) {
            throw "arg cmd_type must be greater than or equal to 0."
        }
        elseif ($SizeOut -lt 0) {
            throw "arg size_out must be greater than or equal to 0."
        }
        elseif ($SizeOut -gt 4096) {
            throw "arg size_out must be less than or equal to 4096."
        }
        elseif ( $null -ne $InData -and $InData.GetType().BaseType.Name -ne "Array" ) {
            throw "arg input must be None or of type array."
        }


        # get the WMI class instance
        $c = [wmiclass]"root\WMI:hpqBDataIn"
        $in_instance = $c.CreateInstance()
        $in_instance.Sign = [System.Text.Encoding]::ASCII.GetBytes("SECU")
        $in_instance.Command = [int]$Command
        $in_instance.CommandType = [int]$CommandType
        $in_instance.Size = 0
        if ( $null -ne $InData) {
            $in_instance.Size = $InData.Length
            $in_instance.hpqBData = $InData
        }

        # get method class instance
        $instance = Get-WmiObject "hpqBIntM" -Namespace "root\WMI" 

        # execute method
        $return_data = $null
        if ($SizeOut -eq 0) {
            $result = $instance.hpqBIOSInt0($in_instance)
        }
        elseif ($SizeOut -le 4) {
            $result = $instance.hpqBIOSInt4($in_instance)
            $return_data = $result.Data
        }
        elseif ($SizeOut -le 128) {
            $result = $instance.hpqBIOSInt128($in_instance)
            $return_data = $result.Data
        }
        elseif ($SizeOut -le 1024) {
            $result = $instance.hpqBIOSInt1024($in_instance)
            $return_data = $result.Data
        }
        elseif ($SizeOut -le 4096) {
            $result = $instance.hpqBIOSInt4096($in_instance)
            $return_data = $result.Data
        }
        else {
            throw "Invalid output data size"
        }
        $returncode = $result.outdata.rwReturnCode
        switch ($returncode) {
            0 { return  $return_data }
            1 { throw "Error: Data pending" }
            2 { throw "Error: Invalid signature" }
            3 { throw "Error: Invalid command" }
            4 { throw "Error: Invalid command type" }
            5 { throw "Error: Invalid data size" }
            6 { throw "Error: Invalid command parameter" }
            28 { throw "Error: Generic failure" }
            4096 { throw "Error: P21 not provisioned" }
            4097 { throw "Error: P21 already provisioned" }
            4098 { throw "Error: P21 in use" }
            default { throw "Error: Unknown return code: $returncode" }
        }
    }
    catch {
        if ($Error[0].CategoryInfo.Activity) {
            $msg = "{0}: {1}" -f $Error[0].CategoryInfo.Activity, $Error[0]
        }
        else {
            $msg = "{0}" -f $Error[0]
        }
        [Console]::Error.WriteLine($msg)
        exit 1
    }
}

function Switch-PowerToAc {
    Invoke-PrivateWmi -Command 2 -CommandType 0x2B -InData (0, 0, 0, 0) -SizeOut 0
    
}

function Switch-PowerToDc {
    Invoke-PrivateWmi -Command 2 -CommandType 0x2B -InData (0, 2, 0, 0) -SizeOut 0
    
}

function Get-PowerStatus {
    try {
        $retry_count = 0
        $PowerStatus = ""
        while ($retry_count -le 5) {
            try {
                $PowerStatus = Get-WmiObject -Class Win32_Battery | Select-Object -ExpandProperty BatteryStatus
                break
            }
            catch {    
                $Error >>  LOGS\powershell.log            
                Write-Log "fail to get power status, retry_count: $retry_count" -Level "DEBUG"                
                $retry_count += 1
                Start-Sleep -Seconds 5
            }
        }
        if ($PowerStatus -eq 1 ) {
            return "DC"

        }
        elseif ($PowerStatus -eq 2 ) {
            return "AC"
        }
        else {
            $Error >>  LOGS\powershell.log
            throw "unexpected status, PowerStatus: '$PowerStatus'"
        }
    }
    catch {
        $Error >>  LOGS\powershell.log
        throw "fail to get power status."
    }


}  


function Switch-ACorDC {

    param (
        [string]$target_powerstatus = ""
    )
    # check status is changed
    if ($target_powerstatus -eq "AC") {
        Switch-PowerToAc
    }
    elseif ($target_powerstatus -eq "DC") {
        Switch-PowerToDc

    }
    else {
        throw "input should AC or DC"
    }


    for ($i = 0; $i -lt 60; $i++) {
        try {
            $powerstatus = Get-PowerStatus
            Write-Log "powerptatus: $powerstatus"
            if ((Get-PowerStatus) -eq $target_powerstatus) {                    
                break
            }
            else {
                Start-Sleep -Seconds 1 #origin 5 seconds
            }    
        }
        catch {            
            Write-Log "retry"
            $Error >>  LOGS\powershell.log
            Start-Sleep -Seconds 5
        }          
        if ($i -eq 59) {
            Write-Log "Fail to switch power status"
            throw "Fail to switch power status"
        }
    }         

    
}
