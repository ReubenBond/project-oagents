from setuptools import setup, find_packages

setup(
    name="microsoft_ai_agents_worker_client",
    version="0.1",
    packages=find_packages(),
    install_requires=[
        # List your dependencies here
    ],
    author="TODO",
    author_email="TODO@TODO.TODO",
    description="Python port of Microsoft.AI.Agents.Worker.Client",
    long_description=open('README.md').read(),
    long_description_content_type='text/markdown',
    url="https://github.com/Microsoft/project-oagents",
    classifiers=[
        "Programming Language :: Python :: 3",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent",
    ],
    python_requires='>=3.11',
)
