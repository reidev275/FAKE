﻿[<AutoOpen>]
module Fake.SqlServer
 
open System
open System.Data.SqlClient
open Microsoft.SqlServer.Management.Smo
open Microsoft.SqlServer.Management.Common
open System.IO

type ServerInfo =
  { Server: Server;
    ConnBuilder: SqlConnectionStringBuilder}
  
/// Gets a connection to the SQL server and an instance to the ConnectionStringBuilder
let getServerInfo connectionString = 
    let connbuilder = new SqlConnectionStringBuilder(connectionString)
    let conn = new ServerConnection()
    if connbuilder.UserID <> "" then
        conn.LoginSecure <- false
        conn.Login <- connbuilder.UserID
    
    if connbuilder.Password <> "" then
        conn.LoginSecure <- false
        conn.Password <- connbuilder.Password
    
    conn.ServerInstance <- connbuilder.DataSource
    conn.Connect()

    {Server = new Server(conn); 
     ConnBuilder = connbuilder}
  
/// gets the DatabaseNames from the server
let getDatabaseNamesFromServer (serverInfo:ServerInfo) = 
    seq {for db in serverInfo.Server.Databases -> db.Name}
                  
/// Checks wether the given Database exists on the server
let existDBOnServer serverInfo dbName = 
    serverInfo
      |> getDatabaseNamesFromServer
      |> Seq.exists ((=) dbName)
    
/// Gets the name of the sercer
let getServerName serverInfo = serverInfo.ConnBuilder.DataSource   

/// Gets the initial catalog name
let getDBName serverInfo = serverInfo.ConnBuilder.InitialCatalog 

/// Gets the initial catalog as database instance
let getDatabase serverInfo = new Database(serverInfo.Server, getDBName serverInfo)
    
/// Checks wether the given InitialCatalog exists on the server    
let intitialCatalogExistsOnServer serverInfo =  
    getDBName serverInfo 
      |> existDBOnServer serverInfo  

/// Drops the given InitialCatalog from the server (if it exists)
let DropDb serverInfo = 
    if intitialCatalogExistsOnServer serverInfo then
        logfn "Dropping database %s on server %s" (getDBName serverInfo) (getServerName serverInfo)
        (getDatabase serverInfo).DropBackupHistory |> ignore
        getDBName serverInfo |> serverInfo.Server.KillDatabase
    serverInfo

/// Kills all Processes
let KillAllProcesses serverInfo =
    serverInfo.Server.KillAllProcesses (getDBName serverInfo)
    serverInfo

/// Detach a database        
let Detach serverInfo =
    serverInfo
      |> KillAllProcesses
      |> fun si -> 
            si.Server.DetachDatabase(getDBName si, true)
            si

/// Attach a database  
let Attach serverInfo (attachOptions:AttachOptions) files =
    let sc = new Collections.Specialized.StringCollection ()
    files |> Seq.iter (fun file -> sc.Add file |> ignore)

    serverInfo.Server.AttachDatabase(getDBName serverInfo,sc,attachOptions)
    serverInfo

/// Creates a new db on the given server
let CreateDb serverInfo =     
    logfn "Creating database %s on server %s" (getDBName serverInfo) (getServerName serverInfo)
    (getDatabase serverInfo).Create()  
    serverInfo
  
/// Runs a sql script on the server
let runScript serverInfo sqlFile =
    logfn "Executing script %s" sqlFile
    sqlFile
      |> StringHelper.ReadFileAsString
      |> (getDatabase serverInfo).ExecuteNonQuery
    
/// Closes the connection to the server
let Disconnect serverInfo = serverInfo.Server.ConnectionContext.Disconnect()

/// Replaces the database files
let ReplaceDatabaseFiles serverInfo targetDir files attachOptions =
    if existDBOnServer serverInfo (getDBName serverInfo) then Detach serverInfo else serverInfo
      |> fun si ->             
            files 
              |> Seq.map (fun fileName ->     
                    let fi = new FileInfo(fileName)
                    CopyFile targetDir fileName
                    targetDir @@ fi.Name)
              |> Attach si attachOptions
      |> Disconnect
 
/// Drops and creates the database (dropped if db exists. created nonetheless)
let DropAndCreateDatabase connectionString = 
    connectionString 
      |> getServerInfo
      |> DropDb
      |> CreateDb
      |> Disconnect          

/// Runs the given sql scripts on the server
let RunScripts connectionString scripts = 
    let serverInfo = getServerInfo connectionString 
    scripts |> Seq.iter (runScript serverInfo)
    Disconnect serverInfo

/// Runs all sql scripts from the given directory on the server  
let RunScriptsFromDirectory connectionString scriptDirectory =
    Directory.GetFiles(scriptDirectory, "*.sql")
      |> RunScripts connectionString  