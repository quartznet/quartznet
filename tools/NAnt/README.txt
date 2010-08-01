NAnt

What is it? 
-----------
NAnt is a .NET based build tool. In theory it is kind of like make without 
make's wrinkles. In practice it's a lot like Ant. 
  
If you are not familiar with Jakarta Ant you can get more information at the
Ant project web site (http://ant.apache.org/).


Why NAnt?
---------
Because Ant was too Java specific.
Because Ant needed the Java runtime.  NAnt only needs the .NET 
or Mono runtime.


The Latest Version
------------------
Details of the latest version can be found on the NAnt project web site
http://nant.sourceforge.net/


Files
-----
  README.txt      - This file.
  Makefile        - Makefile for compilation with GNU Make.
  Makefile.nmake  - Makefile for compilation with Microsoft NMake.


Compilation and Installation
----------------------------

   a. Overview
   -----------
   The compilation process uses NAnt to build NAnt.
   
   The approach is to first compile a copy of NAnt (using make/nmake) for 
   bootstrapping purpose. Next, the bootstrapped version of NAnt is used in 
   conjunction with NAnt build file (NAnt.build) to build the full version.
   
   
   b. Prerequisites
   ----------------
   To build NAnt, you will need the following components:

   Windows
   -------

       * A version of the Microsoft .NET Framework

           Available from http://msdn.microsoft.com/netframework/
         
           You will need the .NET Framework SDK as well as the runtime 
           components if you intend to compile programs.

           Note: NAnt currently supports versions 1.0, 1.1 and 2.0 
           of the Microsoft .NET Framework. 

       or

       * Mono for Windows (version 1.0 or higher)

           Available from http://www.mono-project.com/downloads/
   
   Linux/Unix
   ----------

       * GNU toolchain - including GNU make

       * pkg-config

           Available from: http://www.freedesktop.org/Software/pkgconfig

       * A working Mono installation and development libraries (version 1.0 or higher)

           Available from: http://www.mono-project.com/downloads/

           
    b. Building the Software
    ------------------------
      
    Build NAnt using Microsoft .NET:     

    GNU Make
    --------
        make install MONO= MCS=csc prefix=<installation path> [DESTDIR=<staging path>]

        eg. make install MONO= MCS=csc prefix="c:\Program Files"

    NMake
    -----
        nmake -f Makefile.nmake install prefix=<installation path> [DESTDIR=<staging path>]
    
        eg. nmake -f Makefile.nmake install prefix="c:\Program Files"


    Building NAnt using Mono:

    GNU Make
    --------
        make install prefix=<installation path> [DESTDIR=<staging path>]

        eg. make install prefix="c:\Program Files"

    NMake
    -----
        nmake -f Makefile.nmake install MONO=mono CSC=mcs prefix=<installation path> [DESTDIR=<staging path>]
    
        eg. nmake -f Makefile.nmake install MONO=mono CSC=mcs prefix=/usr/local/

Note: 

These instructions only apply to the source distribution of NAnt, as the binary distribution 
contains pre-built assemblies.


Documentation
-------------
Documentation is available in HTML format, in the doc/ directory.


License
-------
Copyright (C) 2001-2008 Gerry Shaw

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307 USA

As a special exception, the copyright holders of this software give you
permission to link the assemblies with independent modules to produce new
assemblies, regardless of the license terms of these independent modules,
and to copy and distribute the resulting assemblies under terms of your
choice, provided that you also meet, for each linked independent module,
the terms and conditions of the license of that module. An independent
module is a module which is not derived from or based on these assemblies.
If you modify this software, you may extend this exception to your version
of the software, but you are not obligated to do so. If you do not wish to
do so, delete this exception statement from your version. 

A copy of the GNU General Public License is available in the COPYING.txt file 
included with all NAnt distributions.

For more licensing information refer to the GNU General Public License on the 
GNU Project web site.
http://www.gnu.org/copyleft/gpl.html
