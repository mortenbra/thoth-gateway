# thoth-gateway
Thoth Gateway: PL/SQL Gateway Module for Microsoft IIS, similar to mod_plsql and Apex Listener (ORDS)

<h1>Thoth Gateway: PL/SQL Gateway Module for Microsoft IIS</h1>

<p>By <a href="http://ora-00001.blogspot.com/">Morten Braten</a></p>

<h3>What is the Thoth Gateway?</h3>

<blockquote><i>In ancient Egyptian mythology, Thoth was an ibis-headed god who mediated between good and evil, making sure neither had a decisive victory over the other. He also served as scribe of the gods, credited with the invention of writing and alphabets. He is associated with the Eye of Horus because he restored it after a battle between Set and Horus in which the latter god's eye was torn out.</i></blockquote>

<p>The Thoth Gateway is a bridge between an Oracle database and a Microsoft web server. It is an open-source alternative to <a href="http://download.oracle.com/docs/cd/B15897_01/web.1012/b14010/concept.htm">mod_plsql</a> and the <a href="http://download.oracle.com/docs/cd/B19306_01/appdev.102/b14258/d_epg.htm">Embedded PL/SQL Gateway</a>, allowing you to develop <a href="http://download.oracle.com/docs/cd/B28359_01/appdev.111/b28424/adfns_web.htm">PL/SQL web applications</a> using the PL/SQL Web Toolkit (OWA) and Oracle Application Express (Apex), and serve the content using Microsoft's Internet Information Server (IIS).</p>

<img src="http://thoth-gateway.googlecode.com/files/thoth-gateway-architecture.jpg" />

<h3>Why use the Thoth Gateway? What's wrong with mod_plsql or ORDS?</h3>

<p>Nothing is wrong with mod_plsql or ORDS. Those modules are professional-quality, well-tested, and officially supported. However, it requires the Apache webserver (in the case of mod_plsql) or a Java-based web server (in the case of ORDS). That can be a good thing or a bad thing, depending on who you ask. The Thoth Gateway was built as an alternative for those who prefer or require the use of Microsoft's IIS. And being open source, the Thoth Gateway can easily be modified or extended, for whatever reason.</p>


<h3>Core features</h3>

<p>Thoth implements the core features of mod_plsql and the Embedded PL/SQL Gateway. Most importantly, it allows you to run <a href="http://apex.oracle.com/">Oracle Application Express (Apex)</a> applications.</p>

<img src="http://thoth-gateway.googlecode.com/files/apex-running-on-iis-using-thoth.jpg"/>

<ul>
  <li>Web page content generation (HTML, XML, etc.) via the PL/SQL Web Toolkit (OWA)</li>
  <li>File uploads and downloads</li>
  <li>Forward CGI variables to OWA environment</li>
  <li>Process HTTP headers including cookies and redirects</li>
  <li>Basic authentication</li>
  <li>Request validation procedure</li>
  <li>Path alias procedure</li>
  <li>Flexible parameter passing</li>
  <li>Caching of procedure metadata to avoid the describe overhead on subsequent requests</li>
  <li>Debug-style error page and logs</li>
  <li>Database connection pooling</li>
</ul>

<h3>Features in Thoth that are not in mod_plsql</h3>

<ul>
 <li><a href="http://ora-00001.blogspot.com/2012/03/windows-ntlm-sso-with-apex.html">Integrated Windows authentication</a> (if the virtual directory that contains the Thoth Gateway is set up with integrated Windows authentication, you can get the username of the authenticated user via owa_util.get_cgi_env('LOGON_USER'))</li>
 <li>CLOB support (parameter values greater than 32k will automatically be converted to CLOB parameters)</li>
 <li>Inclusion list for procedures (which means you can whitelist procedures instead of blacklisting them, for increased security)</li>
 <li>No limit on the number of parameters (mod_plsql has a limit of 2000 parameters or name/value pairs per procedure)</li>
 <li>Optionally, querystring and form parameters can be passed along with the URL to the PlsqlPathAliasProcedure (which, among other things, means that you can implement complete <a href="http://ora-00001.blogspot.com/2009/07/creating-rest-web-service-with-plsql.html">RESTful web services with PL/SQL</a>; the standard behaviour of mod_plsql is to discard the posted form parameters and just pass along the URL)</li>
 <li>Set maximum individual file size for uploads</li>
 <li><a href="http://ora-00001.blogspot.com/2009/11/publish-plsql-as-soap-web-service.html">Publish PL/SQL as SOAP Web Services</a></li>
 <li><a href="http://ora-00001.blogspot.com/2009/11/more-plsql-gateway-goodies.html">XDB integration</a></li>
</ul>

<h3>Features in mod_plsql that are not in Thoth (yet)</h3>

<ul>
 <li>Custom authentication methods, based on the OWA_SEC package and custom packages. Note that the Thoth Gateway does support basic authentication, but does not implement the OWA-specific authorization procedures. Most applications, including Apex, use cookie-based authentication anyway.</li>
 <li>File system caching of PL/SQL-generated web pages, based on the OWA_CACHE package. Note that the Embedded PL/SQL Gateway (DBMS_EPG) does not support caching, either. However, you can place static files such as images, stylesheets and Javascript in the file system [a separate virtual directory in IIS], and you can employ the built-in functionality in Apex to cache pages and page regions.</li>
 <li>HTML image maps, as represented by the OWA.IMAGE datatype. Who uses client-side image maps these days, anyway? I have not seen this technique used on a web page since the glory days of Netscape Navigator back in 1998...?!?</li>
 <li>Override CGI environment variables in DAD configuration file. Note that the Thoth Gateway passes all CGI variables from the web server to OWA; by default mod_plsql passes just a subset and you have to add additional parameters in the DAD config file.</li>
</ul>


<h3>Getting started</h3>

<p>Download the package and refer to the Installation Guide for details on how to set up the Thoth Gateway.</p>

<h3>Technical details</h3>

<ul>
 <li>The Thoth Gateway is implemented as an <a href="http://msdn.microsoft.com/en-us/library/zec9k340(VS.71).aspx">ASP.NET HttpModule</a> written in C# using the .NET Framework</li>
 <li>The <a href="http://www.oracle.com/technology/tech/windows/odpnet/index.html">Oracle Data Provider for .NET (ODP.NET)</a> is used to communicate with the database</li>
 <li>Logging and instrumentation is done using <a href="http://logging.apache.org/log4net/index.html">log4net</a></li>
 <li>Configuration of Database Access Descriptors (DADs) is stored in the web.config file</li>
</ul>

<h3>Contributions</h3>

<p>If you have .NET or Oracle skills (especially in the areas of performance optimization and security) and would like to contribute to or improve the source code of the Thoth Gateway, please contact me.</p>


<h3>Acknowledgements</h3>

<p>Several other gateway implementations exist, including <a href="http://oss.oracle.com/projects/mod_owa/dist/documentation/modowa.htm">mod_owa</a> (which is an Apache module written by Doug McMahon) and <a href="http://sourceforge.net/projects/dbprism/">DBPrism</a> (which is a Java servlet written by Marcelo Ochoa). The source code and documentation for these other (open-source) implementations have been of invaluable help during the development of the Thoth gateway. The documentation for Tom Kyte's older <a href="http://www.ooug.org/presentations/2005slides/0720/ToolsIuse/owarepl_index.html">OWA Replacement Cartridge</a> and the official <a href="http://download.oracle.com/docs/cd/B15897_01/web.1012/b14010/toc.htm">documentation for mod_plsql</a> itself have also been useful. I'd also like to give a shout-out to the community at <a href="http://www.stackoverflow.com">StackOverflow</a>.</p>
