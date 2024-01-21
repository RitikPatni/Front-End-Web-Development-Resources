#!/bin/sh

dotnetDir="/opt/dotnet"

if [ -d "/etc/dns/config" ]
then
	dnsDir="/etc/dns"
else
    dnsDir="/opt/technitium/dns"
fi

dnsTar="$dnsDir/DnsServerPortable.tar.gz"
dnsUrl="https://download.technitium.com/dns/DnsServerPortable.tar.gz"

mkdir -p $dnsDir
installLog="$dnsDir/install.log"
echo "" > $installLog

echo ""
echo "==============================="
echo "Technitium DNS Server Installer"
echo "==============================="

if dotnet --list-runtimes 2> /dev/null | grep -q "Microsoft.AspNetCore.App 7.0."; 
then
	dotnetFound="yes"
else
	dotnetFound="no"
fi

if [ ! -d $dotnetDir ] && [ "$dotnetFound" = "yes" ]
then
	echo ""
	echo "ASP.NET Core Runtime is already installed."
else
	echo ""

	if [ -d $dotnetDir ] && [ "$dotnetFound" = "yes" ]
	then
		dotnetUpdate="yes"
		echo "Updating ASP.NET Core Runtime..."
	else
		dotnetUpdate="no"
		echo "Installing ASP.NET Core Runtime..."
	fi

	curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin -c 7.0 --runtime aspnetcore --no-path --install-dir $dotnetDir --verbose >> $installLog 2>&1

	if [ ! -f "/usr/bin/dotnet" ]
	then
		ln -s $dotnetDir/dotnet /usr/bin >> $installLog 2>&1
	fi

	if dotnet --list-runtimes 2> /dev/null | grep -q "Microsoft.AspNetCore.App 7.0."; 
	then
		if [ "$dotnetUpdate" = "yes" ]
		then
			echo "ASP.NET Core Runtime was updated successfully!"
		else
			echo "ASP.NET Core Runtime was installed successfully!"
		fi
	else
		echo "Failed to install ASP.NET Core Runtime. Please try again."
		exit 1
	fi
fi

echo ""
echo "Downloading Technitium DNS Server..."

if curl -o $dnsTar --fail $dnsUrl >> $installLog 2>&1
then
	if [ -d $dnsDir ]
	then
		echo "Updating Technitium DNS Server..."
	else
		echo "Installing Technitium DNS Server..."
	fi
	
	tar -zxf $dnsTar -C $dnsDir >> $installLog 2>&1
	
	if [ "$(ps --no-headers -o comm 1 | tr -d '\n')" = "systemd" ] 
	then
		if [ -f "/etc/systemd/system/dns.service" ]
		then
			echo "Restarting systemd service..."
			systemctl restart dns.service >> $installLog 2>&1
		else
			echo "Configuring systemd service..."
			cp $dnsDir/systemd.service /etc/systemd/system/dns.service
			systemctl enable dns.service >> $installLog 2>&1
			
			systemctl stop systemd-resolved >> $installLog 2>&1
			systemctl disable systemd-resolved >> $installLog 2>&1
			
			systemctl start dns.service >> $installLog 2>&1
			
			rm /etc/resolv.conf >> $installLog 2>&1
			echo "nameserver 127.0.0.1" > /etc/resolv.conf 2>> $installLog
			
			if [ -f "/etc/NetworkManager/NetworkManager.conf" ]
			then
				echo "[main]" >> /etc/NetworkManager/NetworkManager.conf
				echo "dns=default" >> /etc/NetworkManager/NetworkManager.conf
			fi
		fi
	
		echo ""
		echo "Technitium DNS Server was installed successfully!"
		echo "Open http://$(hostname):5380/ to access the web console."
		echo ""
		echo "Donate! Make a contribution by becoming a Patron: https://www.patreon.com/technitium"
		echo ""
	else
		echo ""
		echo "Failed to install Technitium DNS Server: systemd was not detected."
		exit 1
	fi
else
	echo ""
	echo "Failed to download Technitium DNS Server from: $dnsUrl"
	exit 1
fi
