module.exports = ({ context, github }) => {
    const { execSync } = require('child_process');
    const fs = require('fs');

    const d = new Date();
    const verGitRevs = execSync('git rev-list 8a657a55.. --count').toString().trim();

    function formatVersion(template) {
        template = template.replaceAll("$(yyyy)", (d.getYear() + 1900).toString());
        template = template.replaceAll("$(yy)", (d.getYear() % 100).toString().padStart(2, '0'));
        template = template.replaceAll("$(mm)", (d.getMonth() + 1).toString().padStart(2, '0'));
        template = template.replaceAll("$(dd)", d.getDate().toString().padStart(2, '0'));
        template = template.replaceAll("$(GitRevCount)", verGitRevs);
        template = template.replaceAll("$(GitSha6)", String(process.env.GITHUB_SHA).substring(0, 6).toLowerCase());
        template = template.replaceAll("$(GitSha8)", String(process.env.GITHUB_SHA).substring(0, 8).toLowerCase());
        template = template.replaceAll("$(GitBranch)", context.ref.replace('refs/heads/', ''));
        template = template.replaceAll("$(RunNumber)", String(process.env.GITHUB_RUN_NUMBER));
        return template;
    }

    function updateJson(filename, updateFunc) {
        var json = JSON.parse(fs.readFileSync(filename, 'utf8'));
        updateFunc(json);
        fs.writeFileSync(filename, JSON.stringify(json));
    }
    function updateText(filename, find, replace) {
        let str = fs.readFileSync(filename, 'utf8');
        if (str.indexOf(find) < 0)
            throw `Unable to find string "${find}" in file "${filename}"`;
        str = str.split(find).join(replace);
        fs.writeFileSync(filename, str);
    }

    const verStr = formatVersion(`1.$(GitRevCount)-$(GitSha6) [$(GitBranch)/$(yyyy)-$(mm)-$(dd)]`);
    const verNum = formatVersion(`1.$(GitRevCount).0`);

    console.log(`Version string: ${verStr}`);
    console.log(`Version number: ${verNum}`);

    return {
        verStr,
        verNum,
        updateJson,
        updateText,
    };
};
